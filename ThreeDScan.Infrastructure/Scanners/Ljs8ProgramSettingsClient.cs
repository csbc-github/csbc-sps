using System.Text.Json;
using Keyence.LjDevCommunication;
using ThreeDScan.Domain.Models;

namespace ThreeDScan.Infrastructure.Scanners;

internal static class Ljs8ProgramConstants
{
    public const int ProgramSettingsBatchSize = 10980;
    public const byte SettingDepthWrite = 0;
    public const byte SettingDepthRunning = 1;
    public const byte SettingDepthSave = 2;
}

internal static class Ljs8TargetBuilder
{
    public static Ljs8IfTargetSetting ProgramItem(byte programNo, byte category, byte item)
    {
        return new Ljs8IfTargetSetting
        {
            _type = (byte)(0x10 + programNo),
            _category = category,
            _item = item,
        };
    }

    public static Ljs8IfTargetSetting ProgramBatch(byte programNo)
        => ProgramItem(programNo, 0xFF, 0x00);
}

internal static class Ljs8SettingBuffers
{
    public static byte[] FourBytes() => new byte[4];

    public static byte At(byte[] data, int index = 0) => data[index];

    public static void Set(byte[] data, byte value, int index = 0) => data[index] = value;

    public static ushort U16(byte[] data, int index = 0)
        => (ushort)(data[index] | (data[index + 1] << 8));

    public static void SetU16(byte[] data, ushort value, int index = 0)
    {
        data[index] = (byte)(value & 0xFF);
        data[index + 1] = (byte)((value >> 8) & 0xFF);
    }

    public static uint U32(byte[] data, int index = 0)
        => (uint)(data[index]
            | (data[index + 1] << 8)
            | (data[index + 2] << 16)
            | (data[index + 3] << 24));

    public static void SetU32(byte[] data, uint value, int index = 0)
    {
        data[index] = (byte)(value & 0xFF);
        data[index + 1] = (byte)((value >> 8) & 0xFF);
        data[index + 2] = (byte)((value >> 16) & 0xFF);
        data[index + 3] = (byte)((value >> 24) & 0xFF);
    }
}

/// <summary>Cat / Item 對照 LJ-S8000 通訊程式庫 §10.3.3（各程式內設定）。</summary>
internal static class Ljs8CaptureSettingTargets
{
    // 拍攝設定：間隔點數（Category 0x03）
    public const byte CatInterval = 0x03;
    public const byte ItemIntervalY = 0x01;          // 1 byte，1~8（1=不縮減）

    // 拍攝設定：曝光 / 動態量程 / 光量控制（Category 0x04）
    public const byte CatImaging = 0x04;
    public const byte ItemExposureTime = 0x00;       // 0~15
    public const byte ItemDynamicRange = 0x01;       // 1~9
    public const byte ItemLightControlMode = 0x02;   // 0:MANUAL 1:AUTO 2:SLOPE
    public const byte ItemLightControlUpper = 0x03;  // 1~99
    public const byte ItemLightControlLower = 0x04;  // 1~99

    // 拍攝設定：峰值 / 半透明（Category 0x05）
    public const byte CatPeak = 0x05;
    public const byte ItemPeakSensitivity = 0x00;    // 1 byte，1~5
    public const byte ItemPeakSelection = 0x01;      // 16 byte（byte0 = 峰值選擇 0~5）
    public const byte ItemTranslucent = 0x02;        // 讀 1 byte；單項 SetSetting 不支援，須批次 blob 修補
    public const byte ItemPeakWidthFilter = 0x03;    // 2 byte

    // 拍攝設定：死角資料插補（Category 0x06，1 byte）
    public const byte CatProfile = 0x06;
    public const byte ItemDeadZoneInterpolation = 0x00; // 0:不插補 1:水平垂直 2:線性

    public const int PeakSelectionDataLength = 16;
    public const int PeakWidthFilterDataLength = 2;
    /// <summary>SetSetting 時各項目的 buffer 長度（§10.3.3 + 實測）。</summary>
    public static int GetSetDataLength(byte category, byte item) => (category, item) switch
    {
        (CatImaging, _) => 4, // 手冊讀 1 byte，SetSetting 實測須 4 byte
        (CatInterval, _) => 4,
        (CatPeak, ItemPeakSelection) => PeakSelectionDataLength,
        (CatPeak, ItemPeakWidthFilter) => 4, // 手冊讀 2 byte（byte0/1），SetSetting 實測須 4 byte
        (CatPeak, _) => 4, // Item00 靈敏度
        (CatProfile, _) => 4,
        _ => 4,
    };
}

/// <summary>
/// LJ Developer DLL 的全域初始化管理。
/// 依手冊規定 Initialize 整個應用程式只能執行一次，且不可在每次操作後 Finalize，
/// 否則 DLL 內部連線狀態會被破壞，導致後續 GetActiveProgram 回 NotOpen(0x1001)。
/// </summary>
internal static class Ljs8Runtime
{
    private static readonly object Gate = new();
    private static bool _initialized;

    /// <summary>
    /// LJ Developer DLL 為行程全域（device id 固定 0），不可並行存取。
    /// 任何開啟連線到關閉連線的操作都必須先取得此鎖，避免兩個操作互相關閉對方的連線
    /// （症狀：連線中途出現 NotOpen(0x1001)）。
    /// </summary>
    private static readonly SemaphoreSlim DeviceGate = new(1, 1);

    public static void EnsureInitialized()
    {
        if (_initialized) return;
        lock (Gate)
        {
            if (_initialized) return;
            Ljs8If.Initialize();
            _initialized = true;
        }
    }

    public static void AcquireDevice() => DeviceGate.Wait();

    public static void ReleaseDevice() => DeviceGate.Release();
}

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
            var hint = rc switch
            {
                0x8032 or 32818 => "（設定資料長度與規定不符，請確認 buffer 大小）",
                0x8048 or 32840 => "（成像處理進行中，請停止量測後再試）",
                32834 or 0x8052 or 32848 or 0x8050 => "（WRITE 緩衝與控制器設定不一致）",
                _ => "",
            };
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

internal static class Ljs8CaptureSettingsCodec
{
    /// <summary>半透明體在程式批次 blob 中的位元組偏移（依 program 快取，避免每次重探）。</summary>
    private static readonly Dictionary<byte, int> TranslucentBatchOffsets = new();
    /// <summary>UI 顯示與 Save 基準皆以 Save 深度為準。</summary>
    public static ScannerCaptureSettings Read(byte programNo, Ljs8DeviceSession session)
        => ReadForSave(programNo, session).Settings;

    /// <summary>Save 基準：LJ-S 拍攝參數以 Save 深度讀取。TriggerReadable 永遠為 false（LJ-S 無對應觸發節距欄位）。</summary>
    public static (ScannerCaptureSettings Settings, bool TriggerReadable) ReadForSave(byte programNo, Ljs8DeviceSession session)
    {
        var settings = ReadCapture(programNo, session, Ljs8ProgramConstants.SettingDepthSave);
        return (settings, false);
    }

    public static ScannerCaptureSettings ReadCapture(byte programNo, Ljs8DeviceSession session, byte depth)
    {
        byte Read1(byte cat, byte item) => session.GetSetting(
            depth, Ljs8TargetBuilder.ProgramItem(programNo, cat, item), 1)[0];

        var exposure = Read1(Ljs8CaptureSettingTargets.CatImaging, Ljs8CaptureSettingTargets.ItemExposureTime);
        var dynamicRange = Read1(Ljs8CaptureSettingTargets.CatImaging, Ljs8CaptureSettingTargets.ItemDynamicRange);
        var lightMode = Read1(Ljs8CaptureSettingTargets.CatImaging, Ljs8CaptureSettingTargets.ItemLightControlMode);
        var lightUpper = Read1(Ljs8CaptureSettingTargets.CatImaging, Ljs8CaptureSettingTargets.ItemLightControlUpper);
        var lightLower = Read1(Ljs8CaptureSettingTargets.CatImaging, Ljs8CaptureSettingTargets.ItemLightControlLower);
        var intervalY = Read1(Ljs8CaptureSettingTargets.CatInterval, Ljs8CaptureSettingTargets.ItemIntervalY);
        var peakSens = Read1(Ljs8CaptureSettingTargets.CatPeak, Ljs8CaptureSettingTargets.ItemPeakSensitivity);
        var translucent = Read1(Ljs8CaptureSettingTargets.CatPeak, Ljs8CaptureSettingTargets.ItemTranslucent);
        var deadZone = Read1(Ljs8CaptureSettingTargets.CatProfile, Ljs8CaptureSettingTargets.ItemDeadZoneInterpolation);

        var peakSelBuf = session.GetSetting(
            depth,
            Ljs8TargetBuilder.ProgramItem(programNo, Ljs8CaptureSettingTargets.CatPeak, Ljs8CaptureSettingTargets.ItemPeakSelection),
            Ljs8CaptureSettingTargets.PeakSelectionDataLength);

        var haloBuf = session.GetSetting(
            depth,
            Ljs8TargetBuilder.ProgramItem(programNo, Ljs8CaptureSettingTargets.CatPeak, Ljs8CaptureSettingTargets.ItemPeakWidthFilter),
            Ljs8CaptureSettingTargets.PeakWidthFilterDataLength);

        return new ScannerCaptureSettings
        {
            ProgramNo = programNo,
            ExposureTimeIndex = exposure,
            DynamicRange = dynamicRange,
            LightControlMode = lightMode,
            LightControlUpper = lightUpper,
            LightControlLower = lightLower,
            YIntervalMultiplier = SdkToUiYInterval(intervalY),
            PeakDetectionSensitivity = peakSens,
            TranslucentPeakProcessing = translucent == 1,
            HaloFilterEnabled = haloBuf[0] == 1,
            HaloFilterStrength = (byte)Math.Clamp((int)haloBuf[1], 1, 5),
            DeadZoneInterpolation = deadZone,
            PeakSelection = peakSelBuf[0],
        };
    }

    private static byte SdkToUiYInterval(byte sdk) => sdk <= 1 ? (byte)0 : sdk;

    private static byte UiToSdkYInterval(byte ui) => ui == 0 ? (byte)1 : ui;

    public static void ApplyCaptureParameters(ScannerCaptureSettings target, ScannerCaptureSettings ui)
    {
        target.DynamicRange = ui.DynamicRange;
        target.ExposureTimeIndex = ui.ExposureTimeIndex;
        target.LightControlMode = ui.LightControlMode;
        target.LightControlUpper = ui.LightControlUpper;
        target.LightControlLower = ui.LightControlLower;
        target.YIntervalMultiplier = ui.YIntervalMultiplier;
        target.PeakDetectionSensitivity = ui.PeakDetectionSensitivity;
        target.TranslucentPeakProcessing = ui.TranslucentPeakProcessing;
        target.HaloFilterEnabled = ui.HaloFilterEnabled;
        target.HaloFilterStrength = ui.HaloFilterStrength;
        target.PeakSelection = ui.PeakSelection;
        target.DeadZoneInterpolation = ui.DeadZoneInterpolation;
        // YDataIntervalMm 僅顯示，不寫入控制器
    }

    public static bool CaptureParametersEqual(ScannerCaptureSettings a, ScannerCaptureSettings b)
        => a.DynamicRange == b.DynamicRange
           && a.ExposureTimeIndex == b.ExposureTimeIndex
           && a.LightControlMode == b.LightControlMode
           && a.LightControlUpper == b.LightControlUpper
           && a.LightControlLower == b.LightControlLower
           && a.YIntervalMultiplier == b.YIntervalMultiplier
           && a.PeakDetectionSensitivity == b.PeakDetectionSensitivity
           && a.TranslucentPeakProcessing == b.TranslucentPeakProcessing
           && a.HaloFilterEnabled == b.HaloFilterEnabled
           && a.HaloFilterStrength == b.HaloFilterStrength
           && a.PeakSelection == b.PeakSelection
           && a.DeadZoneInterpolation == b.DeadZoneInterpolation;

    /// <summary>LJ-S 無對應觸發節距欄位，保留簽章供服務層呼叫，永遠視為相同。</summary>
    public static void ApplyTriggerParameters(ScannerCaptureSettings target, ScannerCaptureSettings ui) { }

    public static bool TriggerParametersEqual(ScannerCaptureSettings a, ScannerCaptureSettings b) => true;

    public static ScannerCaptureSettings Clone(ScannerCaptureSettings source) => new()
    {
        ProgramNo = source.ProgramNo,
        DynamicRange = source.DynamicRange,
        ExposureTimeIndex = source.ExposureTimeIndex,
        LightControlMode = source.LightControlMode,
        LightControlUpper = source.LightControlUpper,
        LightControlLower = source.LightControlLower,
        YIntervalMultiplier = source.YIntervalMultiplier,
        YDataIntervalMm = source.YDataIntervalMm,
        PeakDetectionSensitivity = source.PeakDetectionSensitivity,
        TranslucentPeakProcessing = source.TranslucentPeakProcessing,
        HaloFilterEnabled = source.HaloFilterEnabled,
        HaloFilterStrength = source.HaloFilterStrength,
        PeakSelection = source.PeakSelection,
        DeadZoneInterpolation = source.DeadZoneInterpolation,
    };

    public static void WriteChangesToSession(
        byte programNo,
        ScannerCaptureSettings baseline,
        ScannerCaptureSettings merged,
        Ljs8DeviceSession session,
        bool includeTrigger = true)
    {
        int? translucentOffset = null;
        if (baseline.TranslucentPeakProcessing != merged.TranslucentPeakProcessing)
            translucentOffset = FindTranslucentBatchOffset(programNo, session);

        if (baseline.ExposureTimeIndex != merged.ExposureTimeIndex)
            WriteScalar(session, programNo, Ljs8CaptureSettingTargets.CatImaging, Ljs8CaptureSettingTargets.ItemExposureTime, merged.ExposureTimeIndex);
        if (baseline.DynamicRange != merged.DynamicRange)
            WriteScalar(session, programNo, Ljs8CaptureSettingTargets.CatImaging, Ljs8CaptureSettingTargets.ItemDynamicRange, merged.DynamicRange);
        if (baseline.LightControlMode != merged.LightControlMode)
            WriteScalar(session, programNo, Ljs8CaptureSettingTargets.CatImaging, Ljs8CaptureSettingTargets.ItemLightControlMode, merged.LightControlMode);
        if (baseline.LightControlUpper != merged.LightControlUpper)
            WriteScalar(session, programNo, Ljs8CaptureSettingTargets.CatImaging, Ljs8CaptureSettingTargets.ItemLightControlUpper, merged.LightControlUpper);
        if (baseline.LightControlLower != merged.LightControlLower)
            WriteScalar(session, programNo, Ljs8CaptureSettingTargets.CatImaging, Ljs8CaptureSettingTargets.ItemLightControlLower, merged.LightControlLower);
        if (baseline.YIntervalMultiplier != merged.YIntervalMultiplier)
            WriteScalar(session, programNo, Ljs8CaptureSettingTargets.CatInterval, Ljs8CaptureSettingTargets.ItemIntervalY, UiToSdkYInterval(merged.YIntervalMultiplier));
        if (baseline.PeakDetectionSensitivity != merged.PeakDetectionSensitivity)
            WriteScalar(session, programNo, Ljs8CaptureSettingTargets.CatPeak, Ljs8CaptureSettingTargets.ItemPeakSensitivity, merged.PeakDetectionSensitivity);

        if (baseline.PeakSelection != merged.PeakSelection)
            WritePeakSelection(session, programNo, merged.PeakSelection);
        if (baseline.HaloFilterEnabled != merged.HaloFilterEnabled
            || baseline.HaloFilterStrength != merged.HaloFilterStrength)
            WriteHaloFilter(session, programNo, merged.HaloFilterEnabled, merged.HaloFilterStrength);

        if (baseline.DeadZoneInterpolation != merged.DeadZoneInterpolation)
            WriteScalar(session, programNo, Ljs8CaptureSettingTargets.CatProfile, Ljs8CaptureSettingTargets.ItemDeadZoneInterpolation, merged.DeadZoneInterpolation);

        // 半透明體單項 SetSetting 皆 0x8032；須於其他逐項寫入完成後，以批次 blob 修補。
        if (baseline.TranslucentPeakProcessing != merged.TranslucentPeakProcessing)
        {
            if (translucentOffset is null)
                throw new InvalidOperationException(
                    "無法在程式批次設定中定位半透明體位元組（Cat 05 Item 02 不支援單項 SetSetting）");
            WriteTranslucentViaBatch(programNo, session, translucentOffset.Value, merged.TranslucentPeakProcessing);
        }
    }

    private static void WriteScalar(Ljs8DeviceSession session, byte programNo, byte cat, byte item, byte value)
    {
        var length = Ljs8CaptureSettingTargets.GetSetDataLength(cat, item);
        var data = new byte[length];
        data[0] = value;
        session.SetWrite(Ljs8TargetBuilder.ProgramItem(programNo, cat, item), data);
    }

    /// <summary>
    /// 在 10980-byte 程式批次中定位半透明體位元組：以 SAVE 讀值比對 blob，
    /// 並在峰值選擇 16-byte 區塊附近縮小候選，再以 SetBatchWrite 探測驗證。
    /// </summary>
    private static int? FindTranslucentBatchOffset(byte programNo, Ljs8DeviceSession session)
    {
        var target = Ljs8TargetBuilder.ProgramItem(
            programNo, Ljs8CaptureSettingTargets.CatPeak, Ljs8CaptureSettingTargets.ItemTranslucent);

        byte current;
        try
        {
            current = session.GetSetting(Ljs8ProgramConstants.SettingDepthSave, target, 1)[0];
        }
        catch
        {
            return null;
        }

        var saveBatch = session.GetBatch(Ljs8ProgramConstants.SettingDepthSave, programNo);

        if (TranslucentBatchOffsets.TryGetValue(programNo, out var cached)
            && cached >= 0
            && cached < saveBatch.Length
            && saveBatch[cached] == current)
        {
            // #region agent log
            Ljs8AgentDebugLog.Write(
                "FindTranslucentBatchOffset",
                "cache hit",
                new { idx = cached, current },
                "H4-batch");
            // #endregion
            return cached;
        }

        var peakPattern = session.GetSetting(
            Ljs8ProgramConstants.SettingDepthSave,
            Ljs8TargetBuilder.ProgramItem(
                programNo,
                Ljs8CaptureSettingTargets.CatPeak,
                Ljs8CaptureSettingTargets.ItemPeakSelection),
            Ljs8CaptureSettingTargets.PeakSelectionDataLength);

        var peakBlock = FindEmbeddedBlockOffset(saveBatch, peakPattern);
        List<int> candidates;
        if (peakBlock is int pb)
        {
            var from = Math.Max(0, pb);
            var to = Math.Min(saveBatch.Length, pb + Ljs8CaptureSettingTargets.PeakSelectionDataLength + 48);
            candidates = FindByteOffsets(saveBatch, current, from, to);

            // 實測半透明體緊接在峰值選擇 16-byte 區塊之後（peakBlock + 16）。
            var preferred = pb + Ljs8CaptureSettingTargets.PeakSelectionDataLength;
            if (preferred < saveBatch.Length && saveBatch[preferred] == current)
                candidates = PrioritizeOffset(candidates, preferred);
        }
        else
        {
            candidates = FindByteOffsets(saveBatch, current);
        }

        if (candidates.Count == 0)
        {
            // #region agent log
            Ljs8AgentDebugLog.Write(
                "FindTranslucentBatchOffset",
                "no candidates",
                new { current, peakBlock },
                "H4-batch");
            // #endregion
            return null;
        }

        var probe = (byte)(current == 0 ? 1 : 0);
        var baselineWrite = session.GetBatch(Ljs8ProgramConstants.SettingDepthWrite, programNo);

        foreach (var idx in candidates)
        {
            var trial = (byte[])baselineWrite.Clone();
            trial[idx] = probe;

            if (!session.TrySetBatchWrite(programNo, trial, out var rc, out _))
            {
                // #region agent log
                Ljs8AgentDebugLog.Write(
                    "FindTranslucentBatchOffset",
                    "probe SetBatchWrite rejected",
                    new { idx, probe, rc },
                    "H4-batch");
                // #endregion
                session.RewriteWriteBufferFrom(Ljs8ProgramConstants.SettingDepthSave);
                baselineWrite = session.GetBatch(Ljs8ProgramConstants.SettingDepthWrite, programNo);
                continue;
            }

            if (session.TryGetSetting(Ljs8ProgramConstants.SettingDepthWrite, target, 1, out var rb)
                && rb[0] == probe)
            {
                // #region agent log
                Ljs8AgentDebugLog.Write(
                    "FindTranslucentBatchOffset",
                    "found",
                    new { idx, current, probe, candidateCount = candidates.Count, peakBlock },
                    "H4-batch");
                // #endregion
                TranslucentBatchOffsets[programNo] = idx;
                session.RewriteWriteBufferFrom(Ljs8ProgramConstants.SettingDepthSave);
                return idx;
            }

            session.RewriteWriteBufferFrom(Ljs8ProgramConstants.SettingDepthSave);
            baselineWrite = session.GetBatch(Ljs8ProgramConstants.SettingDepthWrite, programNo);
        }

        session.RewriteWriteBufferFrom(Ljs8ProgramConstants.SettingDepthSave);

        // #region agent log
        Ljs8AgentDebugLog.Write(
            "FindTranslucentBatchOffset",
            "probe exhausted",
            new { current, candidateCount = candidates.Count, peakBlock },
            "H4-batch");
        // #endregion
        return null;
    }

    private static List<int> PrioritizeOffset(List<int> candidates, int preferred)
    {
        if (candidates.Contains(preferred))
            return [preferred, .. candidates.Where(c => c != preferred)];
        return candidates;
    }

    private static void WriteTranslucentViaBatch(
        byte programNo,
        Ljs8DeviceSession session,
        int offset,
        bool enabled)
    {
        var batch = session.GetBatch(Ljs8ProgramConstants.SettingDepthWrite, programNo);
        var value = (byte)(enabled ? 1 : 0);
        batch[offset] = value;

        // #region agent log
        Ljs8AgentDebugLog.Write(
            "WriteTranslucentViaBatch",
            "SetBatchWrite patch",
            new { offset, value, batchLen = batch.Length },
            "H4-batch");
        // #endregion

        session.SetBatchWrite(programNo, batch);

        var target = Ljs8TargetBuilder.ProgramItem(
            programNo, Ljs8CaptureSettingTargets.CatPeak, Ljs8CaptureSettingTargets.ItemTranslucent);
        if (!session.TryGetSetting(Ljs8ProgramConstants.SettingDepthWrite, target, 1, out var rb)
            || rb[0] != value)
        {
            throw new InvalidOperationException(
                $"半透明體批次修補後讀回不符（offset={offset}，預期={value}，實際={(rb.Length > 0 ? rb[0].ToString() : "?")}）");
        }
    }

    private static int? FindEmbeddedBlockOffset(byte[] batch, byte[] pattern)
    {
        if (pattern.Length == 0 || batch.Length < pattern.Length)
            return null;

        for (var i = 0; i <= batch.Length - pattern.Length; i++)
        {
            if (batch.AsSpan(i, pattern.Length).SequenceEqual(pattern))
                return i;
        }

        return null;
    }

    private static List<int> FindByteOffsets(byte[] batch, byte value, int start = 0, int end = int.MaxValue)
    {
        var list = new List<int>();
        var to = Math.Min(end, batch.Length);
        for (var i = Math.Max(0, start); i < to; i++)
        {
            if (batch[i] == value)
                list.Add(i);
        }

        return list;
    }

    /// <summary>峰值選擇為 16 byte 結構，僅 byte0 為峰值選擇；以讀-改-寫保留其餘設定。</summary>
    private static void WritePeakSelection(Ljs8DeviceSession session, byte programNo, byte selection)
    {
        var target = Ljs8TargetBuilder.ProgramItem(
            programNo, Ljs8CaptureSettingTargets.CatPeak, Ljs8CaptureSettingTargets.ItemPeakSelection);

        if (!session.TryGetSetting(
                Ljs8ProgramConstants.SettingDepthWrite,
                target,
                Ljs8CaptureSettingTargets.PeakSelectionDataLength,
                out var buf))
        {
            if (!session.TryGetSetting(
                    Ljs8ProgramConstants.SettingDepthSave,
                    target,
                    Ljs8CaptureSettingTargets.PeakSelectionDataLength,
                    out buf))
            {
                buf = new byte[Ljs8CaptureSettingTargets.PeakSelectionDataLength];
            }
        }

        buf[0] = selection;
        session.SetWrite(target, buf);
    }

    private static void WriteHaloFilter(Ljs8DeviceSession session, byte programNo, bool enabled, byte strength)
    {
        var length = Ljs8CaptureSettingTargets.GetSetDataLength(
            Ljs8CaptureSettingTargets.CatPeak, Ljs8CaptureSettingTargets.ItemPeakWidthFilter);
        var data = new byte[length];
        data[0] = (byte)(enabled ? 1 : 0);
        data[1] = (byte)Math.Clamp((int)strength, 1, 5);
        session.SetWrite(
            Ljs8TargetBuilder.ProgramItem(
                programNo, Ljs8CaptureSettingTargets.CatPeak, Ljs8CaptureSettingTargets.ItemPeakWidthFilter),
            data);
    }
}

internal static class Ljs8AgentDebugLog
{
    private const string LogPath = @"d:\Project\CSBS\角鋼\Program\ThreeDScan\debug-710818.log";

    // #region agent log
    public static void Write(string location, string message, object data, string hypothesisId)
    {
        try
        {
            var line = JsonSerializer.Serialize(new
            {
                sessionId = "710818",
                runId = "post-fix",
                hypothesisId,
                location,
                message,
                data,
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            });
            File.AppendAllText(LogPath, line + Environment.NewLine);
        }
        catch
        {
            // ignore debug log failures
        }
    }
    // #endregion
}
