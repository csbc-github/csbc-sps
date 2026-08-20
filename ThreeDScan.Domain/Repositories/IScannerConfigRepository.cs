using ThreeDScan.Domain.Models;

namespace ThreeDScan.Domain.Repositories;

/// <summary>ScannerConfig 持久化（每台掃描器一筆 Ref 與量測參數）。</summary>
public interface IScannerConfigRepository
{
    /// <summary>依 ScannerId 讀取設定；不存在回傳 null。</summary>
    Task<ScannerConfigRecord?> GetAsync(string scannerId, CancellationToken ct = default);

    /// <summary>新增或更新 ScannerConfig 記錄。</summary>
    Task UpsertAsync(ScannerConfigRecord record, CancellationToken ct = default);
}
