using ThreeDScan.Domain.Models;

namespace ThreeDScan.Domain.Repositories;

/// <summary>線掃描器兩點桌面與 ROI 設定（DB）。</summary>
public interface ILineScannerSettingsRepository
{
    /// <summary>讀取設定；不存在回傳 null。</summary>
    Task<LineScannerSettingsRecord?> GetAsync(string scannerId, CancellationToken ct = default);

    /// <summary>覆寫該掃描器設定。</summary>
    Task SaveAsync(LineScannerSettingsRecord record, CancellationToken ct = default);
}
