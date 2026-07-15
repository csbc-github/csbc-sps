using ThreeDScan.Domain.Abstractions;
using ThreeDScan.Domain.Models;
using ThreeDScan.Infrastructure.Configuration;

namespace ThreeDScan.Infrastructure.Scanners;

/// <summary>掃描器拍攝參數讀寫：LJ 控制器與資料庫備份同步。</summary>
public sealed class ScannerProgramSettingsService : IScannerProgramSettingsService
{
    private readonly IScannerRegistry _registry;
    private readonly IScannerProgramSettingsStore _store;

    public ScannerProgramSettingsService(
        IScannerRegistry registry,
        IScannerProgramSettingsStore store)
    {
        _registry = registry;
        _store = store;
    }

    /// <inheritdoc />
    public Task<ScannerCaptureSettings> ReadFromControllerAsync(
        string scannerId,
        byte programNo,
        CancellationToken ct = default)
        => Task.Run(() => ReadFromControllerCore(scannerId, programNo), ct);

    /// <inheritdoc />
    public Task SaveToControllerAsync(
        string scannerId,
        byte programNo,
        ScannerCaptureSettings settings,
        string? updatedBy = null,
        CancellationToken ct = default)
        => Task.Run(() => SaveToControllerCore(scannerId, programNo, settings, updatedBy), ct);

    /// <inheritdoc />
    public Task<ScannerCaptureSettings> ReadFromDatabaseAsync(
        string scannerId,
        byte programNo,
        CancellationToken ct = default)
        => Task.Run(() => ReadFromDatabaseCore(scannerId, programNo), ct);

    /// <inheritdoc />
    public Task ApplyRunningSettingsAsync(
        string scannerId,
        byte programNo,
        ScannerCaptureSettings settings,
        CancellationToken ct = default)
        => Task.Run(() => ApplyRunningSettingsCore(scannerId, programNo, settings), ct);

    /// <inheritdoc />
    public Task<byte[]?> GetStoredBlobAsync(
        string scannerId,
        byte programNo,
        CancellationToken ct = default)
        => _store.GetBlobAsync(scannerId, programNo, ct);

    private ScannerCaptureSettings ReadFromControllerCore(string scannerId, byte programNo)
    {
        using var session = OpenSession(scannerId);
        session.EnsureProgram(programNo);
        return Ljs8CaptureSettingsCodec.Read(programNo, session);
    }

    private void SaveToControllerCore(
        string scannerId,
        byte programNo,
        ScannerCaptureSettings uiSettings,
        string? updatedBy)
    {
        using var session = OpenSession(scannerId);
        session.EnsureProgram(programNo);
        session.EnsureMemoryReady();

        var (original, triggerReadable) = Ljs8CaptureSettingsCodec.ReadForSave(programNo, session);
        var merged = Ljs8CaptureSettingsCodec.Clone(original);
        Ljs8CaptureSettingsCodec.ApplyCaptureParameters(merged, uiSettings);
        if (triggerReadable)
            Ljs8CaptureSettingsCodec.ApplyTriggerParameters(merged, uiSettings);

        var captureUnchanged = Ljs8CaptureSettingsCodec.CaptureParametersEqual(original, merged);
        var triggerUnchanged = !triggerReadable
            || Ljs8CaptureSettingsCodec.TriggerParametersEqual(original, merged);
        if (captureUnchanged && triggerUnchanged)
        {
            PersistSaveBlob(scannerId, programNo, session, updatedBy, "UiSaveUnchanged");
            return;
        }

        // 以 SDK 正規方式把 WRITE 緩衝同步成 SAVE 內容，再逐項覆寫變更的項目。
        // （改用整批灌入會讓 WRITE 緩衝進入不一致狀態，導致逐項寫入被控制器以 0x8032 拒絕。）
        session.RewriteWriteBufferFrom(Ljs8ProgramConstants.SettingDepthSave);
        Ljs8CaptureSettingsCodec.WriteChangesToSession(
            programNo,
            original,
            merged,
            session,
            includeTrigger: triggerReadable);

        session.ReflectSave();
        session.WaitForSaveComplete();

        // 測試掃描與量測使用 RUNNING；Save 後必須把 SAVE 同步到 RUNNING 才會生效。
        session.SyncRunningFromSave();

        PersistSaveBlob(scannerId, programNo, session, updatedBy, "UiSave");
    }

    private void PersistSaveBlob(
        string scannerId,
        byte programNo,
        Ljs8DeviceSession session,
        string? updatedBy,
        string source)
    {
        var blob = session.GetBatch(Ljs8ProgramConstants.SettingDepthSave, programNo);
        var traceId = Guid.NewGuid().ToString("N");
        _store.UpsertWithHistoryAsync(
            scannerId,
            programNo,
            blob,
            source,
            updatedBy,
            traceId,
            CancellationToken.None).GetAwaiter().GetResult();
    }

    private ScannerCaptureSettings ReadFromDatabaseCore(string scannerId, byte programNo)
    {
        var blob = _store.GetBlobAsync(scannerId, programNo, CancellationToken.None)
            .GetAwaiter().GetResult();
        if (blob is null || blob.Length == 0)
            throw new InvalidOperationException(
                $"「{scannerId}」群組 program {programNo} 尚無資料庫備份。請先按「套用並 Save」建立備份。");

        // 僅灌入 SDK WRITE 緩衝以解讀 blob，不 Reflect Save／Running，關閉連線後不影響控制器。
        using var session = OpenSession(scannerId);
        session.EnsureProgram(programNo);
        session.SetBatchWrite(programNo, blob);
        return Ljs8CaptureSettingsCodec.ReadCapture(
            programNo,
            session,
            Ljs8ProgramConstants.SettingDepthWrite);
    }

    private void ApplyRunningSettingsCore(
        string scannerId,
        byte programNo,
        ScannerCaptureSettings uiSettings)
    {
        using var session = OpenSession(scannerId);
        session.EnsureProgram(programNo);
        session.EnsureMemoryReady();

        // 控制器不接受逐項讀取 Running，改以 Save 內容為基準合併變更。
        var original = Ljs8CaptureSettingsCodec.ReadCapture(
            programNo,
            session,
            Ljs8ProgramConstants.SettingDepthSave);

        if (Ljs8CaptureSettingsCodec.CaptureParametersEqual(original, uiSettings))
            return;

        var merged = Ljs8CaptureSettingsCodec.Clone(original);
        Ljs8CaptureSettingsCodec.ApplyCaptureParameters(merged, uiSettings);

        session.RewriteWriteBufferFrom(Ljs8ProgramConstants.SettingDepthSave);
        Ljs8CaptureSettingsCodec.WriteChangesToSession(
            programNo,
            original,
            merged,
            session,
            includeTrigger: false);
        session.ReflectRunning();
    }

    private Ljs8DeviceSession OpenSession(string scannerId)
    {
        if (!_registry.TryGetEndpoint(scannerId, out var endpoint))
            throw new InvalidOperationException($"未知 ScannerId: {scannerId}");

        var ipParts = endpoint.Ip.Split('.');
        if (ipParts.Length != 4)
            throw new InvalidOperationException($"Scanner {scannerId} IP 格式錯誤: {endpoint.Ip}");

        var ip = new byte[]
        {
            byte.Parse(ipParts[0]),
            byte.Parse(ipParts[1]),
            byte.Parse(ipParts[2]),
            byte.Parse(ipParts[3]),
        };

        var session = new Ljs8DeviceSession(ip, endpoint.Port);
        session.Open();
        return session;
    }
}
