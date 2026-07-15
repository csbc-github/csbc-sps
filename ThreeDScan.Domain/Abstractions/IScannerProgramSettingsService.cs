using ThreeDScan.Domain.Models;

namespace ThreeDScan.Domain.Abstractions;

/// <summary>掃描器拍攝參數服務：讀寫 LJ 控制器設定，並與資料庫備份同步。</summary>
public interface IScannerProgramSettingsService
{
    /// <summary>從 LJ 控制器讀取指定群組的拍攝參數（Read 緩衝）。</summary>
    Task<ScannerCaptureSettings> ReadFromControllerAsync(
        string scannerId,
        byte programNo,
        CancellationToken ct = default);

    /// <summary>將拍攝參數 Save 至 LJ 控制器，並寫入資料庫備份與歷程。</summary>
    Task SaveToControllerAsync(
        string scannerId,
        byte programNo,
        ScannerCaptureSettings settings,
        string? updatedBy = null,
        CancellationToken ct = default);

    /// <summary>從資料庫備份解讀拍攝參數（僅供 UI 顯示，不寫入控制器）。</summary>
    Task<ScannerCaptureSettings> ReadFromDatabaseAsync(
        string scannerId,
        byte programNo,
        CancellationToken ct = default);

    /// <summary>將設定寫入 Write 緩衝並 Reflect 至 RUNNING（不 Save），供測試掃描使用。</summary>
    Task ApplyRunningSettingsAsync(
        string scannerId,
        byte programNo,
        ScannerCaptureSettings settings,
        CancellationToken ct = default);

    /// <summary>取得資料庫中該群組的原始設定二進位 Blob；無備份時回傳 null。</summary>
    Task<byte[]?> GetStoredBlobAsync(
        string scannerId,
        byte programNo,
        CancellationToken ct = default);
}
