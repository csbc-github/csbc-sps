using Keyence.LjDevCommunication;

namespace ThreeDScan.Infrastructure.Scanners;

internal sealed class Ljs8DeviceSession : IDisposable
{
    private readonly byte[] _ip;
    private readonly int _port;
    private bool _open;

    public Ljs8DeviceSession(byte[] ipAddress, int port)
    {
        _ip = ipAddress;
        _port = port;
    }

    public void Open()
    {
        if (_open) return;
        Ljs8Runtime.EnsureInitialized();
        Ljs8Runtime.AcquireDevice();
        try
        {
            var rc = Ljs8If.CommunicationOpen(0, _ip, (ushort)_port);
            if (rc != 0)
                throw new InvalidOperationException($"CommunicationOpen 錯誤碼 {rc}");
            _open = true;
        }
        catch
        {
            Ljs8Runtime.ReleaseDevice();
            throw;
        }
    }

    public void EnsureProgram(byte programNo)
    {
        byte active = 0;
        var rc = Ljs8If.GetActiveProgram(0, ref active);
        if (rc != 0)
            throw new InvalidOperationException($"GetActiveProgram 錯誤碼 {rc}");

        if (active == programNo) return;

        var changeRc = Ljs8If.ChangeActiveProgram(0, programNo);
        Thread.Sleep(200);

        rc = Ljs8If.GetActiveProgram(0, ref active);
        if (changeRc != 0)
            throw new InvalidOperationException($"ChangeActiveProgram 錯誤碼 {changeRc}");
        if (active != programNo)
            throw new InvalidOperationException($"無法切換至程式 {programNo}，目前為 {active}");
    }

    public byte[] GetSetting(byte depth, Ljs8IfTargetSetting target, int dataLength)
    {
        var data = new byte[dataLength];
        var rc = Ljs8If.GetSetting(0, depth, target, ref data);
        if (rc != 0)
            throw new InvalidOperationException(FormatGetSettingError(rc, depth, target));
        return data;
    }

    public bool TryGetSetting(byte depth, Ljs8IfTargetSetting target, int dataLength, out byte[] data)
    {
        data = new byte[dataLength];
        var rc = Ljs8If.GetSetting(0, depth, target, ref data);
        return rc == 0;
    }

    private static string FormatGetSettingError(int rc, byte depth, Ljs8IfTargetSetting target)
    {
        var hint = rc == 0x8042
            ? "（參數錯誤：Cat/Item 或資料長度不符）"
            : rc == 0x8051
                ? "（感測頭啟動中，請待約 50 秒後再試）"
                : "";
        return $"GetSetting 錯誤碼 {rc} (Cat {target._category:X2} Item {target._item:X2}){hint}";
    }

    public byte[] GetBatch(byte depth, byte programNo)
    {
        var data = new byte[Ljs8ProgramConstants.ProgramSettingsBatchSize];
        var rc = Ljs8If.GetSetting(0, depth, Ljs8TargetBuilder.ProgramBatch(programNo), ref data);
        if (rc != 0)
            throw new InvalidOperationException($"GetSetting 批次讀取錯誤碼 {rc}");
        return data;
    }

    public void SetWrite(Ljs8IfTargetSetting target, byte[] data)
    {
        // #region agent log
        Ljs8AgentDebugLog.Write(
            "Ljs8DeviceSession.SetWrite",
            "SetSetting(Write) before",
            new
            {
                cat = target._category,
                item = target._item,
                type = target._type,
                length = data.Length,
                b0 = data.Length > 0 ? data[0] : (byte?)null,
                b1 = data.Length > 1 ? data[1] : (byte?)null,
                b2 = data.Length > 2 ? data[2] : (byte?)null,
                b3 = data.Length > 3 ? data[3] : (byte?)null,
            },
            target._category == 0x05 && (target._item == 0x02 || target._item == 0x03) ? "H2-exact-len" : "H0");
        // #endregion

        uint error = 0;
        var rc = Ljs8If.SetSetting(0, Ljs8ProgramConstants.SettingDepthWrite, target, ref data, ref error);

        // #region agent log
        Ljs8AgentDebugLog.Write(
            "Ljs8DeviceSession.SetWrite",
            rc == 0 ? "SetSetting(Write) ok" : "SetSetting(Write) failed",
            new { rc, error, cat = target._category, item = target._item, length = data.Length },
            target._category == 0x05 && (target._item == 0x02 || target._item == 0x03) ? "H2-exact-len" : "H0");
        // #endregion

        if (rc != 0)
        {
            string hint;
            switch (rc)
            {
                case 0x8032: // 32818
                    hint = "（設定資料長度與規定不符，請確認 buffer 大小）";
                    break;
                case 0x8048: // 32840
                    hint = "（成像處理進行中，請停止量測後再試）";
                    break;
                case 0x8052: // 32834
                case 0x8050: // 32848
                    hint = "（WRITE 緩衝與控制器設定不一致）";
                    break;
                default:
                    hint = "";
                    break;
            }
            throw new InvalidOperationException(
                $"SetSetting(Write) 錯誤碼 {rc} (Cat {target._category:X2} Item {target._item:X2}){hint}");
        }
        if (error != 0)
            throw new InvalidOperationException($"SetSetting 詳細錯誤 0x{error:X8}");
    }

    /// <summary>不拋例外的 SetSetting，供半透明體等多長度探測。</summary>
    public bool TrySetWrite(Ljs8IfTargetSetting target, byte[] data, out int rc, out uint error)
    {
        error = 0;
        rc = Ljs8If.SetSetting(0, Ljs8ProgramConstants.SettingDepthWrite, target, ref data, ref error);
        return rc == 0 && error == 0;
    }

    public void SetBatchWrite(byte programNo, byte[] blob)
    {
        if (!TrySetBatchWrite(programNo, blob, out var rc, out var error))
            throw new InvalidOperationException($"SetSetting(Write) 批次錯誤碼 {rc}");
    }

    public bool TrySetBatchWrite(byte programNo, byte[] blob, out int rc, out uint error)
    {
        error = 0;
        rc = Ljs8If.SetSetting(
            0,
            Ljs8ProgramConstants.SettingDepthWrite,
            Ljs8TargetBuilder.ProgramBatch(programNo),
            ref blob,
            ref error);
        return rc == 0 && error == 0;
    }

    public void SetBatchSave(byte programNo, byte[] blob)
    {
        uint error = 0;
        var rc = Ljs8If.SetSetting(
            0,
            Ljs8ProgramConstants.SettingDepthSave,
            Ljs8TargetBuilder.ProgramBatch(programNo),
            ref blob,
            ref error);
        if (rc != 0)
            throw new InvalidOperationException($"SetSetting(Save) 批次錯誤碼 {rc}");
        if (error != 0)
            throw new InvalidOperationException($"SetSetting(Save) 詳細錯誤 0x{error:X8}");
    }

    public void RewriteWriteBufferFrom(byte depth)
    {
        var rc = Ljs8If.RewriteTemporarySetting(0, depth);
        if (rc != 0)
            throw new InvalidOperationException($"RewriteTemporarySetting 錯誤碼 {rc}（無法以深度 {depth} 初始化 WRITE 緩衝）");
    }

    public void ReflectSave()
    {
        uint error = 0;
        var rc = Ljs8If.ReflectSetting(0, Ljs8ProgramConstants.SettingDepthSave, ref error);
        if (rc != 0)
            throw new InvalidOperationException($"ReflectSetting(Save) 錯誤碼 {rc}");
        if (error != 0)
            throw new InvalidOperationException($"ReflectSetting 詳細錯誤 0x{error:X8}");
    }

    public void ReflectRunning()
    {
        uint error = 0;
        var rc = Ljs8If.ReflectSetting(0, Ljs8ProgramConstants.SettingDepthRunning, ref error);
        if (rc != 0)
            throw new InvalidOperationException($"ReflectSetting(Running) 錯誤碼 {rc}");
        if (error != 0)
            throw new InvalidOperationException($"ReflectSetting 詳細錯誤 0x{error:X8}");
    }

    /// <summary>盡力反映至動作中設定；失敗回傳 false，不擲例外。</summary>
    public bool TryReflectRunning()
    {
        uint error = 0;
        var rc = Ljs8If.ReflectSetting(0, Ljs8ProgramConstants.SettingDepthRunning, ref error);
        return rc == 0 && error == 0;
    }

    /// <summary>將 SAVE 區已儲存的程式設定同步至 RUNNING（測試掃描使用 RUNNING）。</summary>
    public void SyncRunningFromSave()
    {
        RewriteWriteBufferFrom(Ljs8ProgramConstants.SettingDepthSave);
        ReflectRunning();
    }

    public void EnsureMemoryReady()
    {
        for (var i = 0; i < 200; i++)
        {
            byte busy = 1;
            Ljs8If.CheckMemoryAccess(0, ref busy);
            if (busy == 0) return;
            Thread.Sleep(50);
        }

        throw new InvalidOperationException(
            "控制器記憶體忙碌（可能 Navigator 正在連線）。請關閉 Navigator 後再試。");
    }

    public void WaitForSaveComplete()
    {
        for (var i = 0; i < 200; i++)
        {
            byte busy = 1;
            Ljs8If.CheckMemoryAccess(0, ref busy);
            if (busy == 0) return;
            Thread.Sleep(50);
        }

        throw new InvalidOperationException("等待控制器儲存完成逾時");
    }

    public void Dispose()
    {
        if (!_open) return;
        try
        {
            Ljs8If.CommunicationClose(0);
            // 不呼叫 Finalize：DLL 僅在程式結束時釋放，避免破壞全域連線狀態。
        }
        finally
        {
            _open = false;
            Ljs8Runtime.ReleaseDevice();
        }
    }
}
