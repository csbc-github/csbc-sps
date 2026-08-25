namespace ThreeDScan.Domain.Repositories;

/// <summary>掃描器拍攝參數二進位 Blob 持久化與歷程。</summary>
public interface IScannerProgramSettingsRepository
{
    /// <summary>寫入或更新當前設定，並追加一筆歷程記錄。</summary>
    Task UpsertWithHistoryAsync(
        string scannerId,
        byte programNo,
        byte[] settingsBlob,
        string source,
        string? updatedBy,
        string traceId,
        CancellationToken ct = default);

    /// <summary>讀取該群組的設定 Blob；無資料時回傳 null。</summary>
    Task<byte[]?> GetBlobAsync(string scannerId, byte programNo, CancellationToken ct = default);
}
