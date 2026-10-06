using ThreeDScan.Domain.Models;

namespace ThreeDScan.Domain.Repositories;

/// <summary>滾輪校正基準的讀寫。每台光達保留最後一次成功結果。</summary>
public interface IRollerCalibrationRepository
{
    /// <summary>讀取該光達最後一次成功校正；沒有則回傳 null。</summary>
    Task<RollerBaselineRecord?> GetAsync(string scannerId, CancellationToken ct = default);

    /// <summary>覆寫該光達的滾輪基準。</summary>
    Task SaveAsync(RollerBaselineRecord record, CancellationToken ct = default);
}
