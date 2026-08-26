using ThreeDScan.Domain.Models;

namespace ThreeDScan.Domain.Repositories;

/// <summary>
/// 工單兩翼厚度：依 <c>jobId</c> 讀寫。
/// </summary>
public interface IJobThicknessRepository
{
    /// <summary>
    /// 依工單號讀取兩翼厚度。
    /// </summary>
    /// <param name="jobId">工單號；空白則視為找不到。</param>
    /// <param name="ct">取消權杖。</param>
    /// <returns>有紀錄則回傳該筆；沒有則 <c>null</c>。</returns>
    Task<JobThicknessRecord?> GetByJobIdAsync(string jobId, CancellationToken ct = default);

    /// <summary>
    /// 寫入或覆寫該工單的厚度。同一 <c>jobId</c> 覆寫為最新一筆。
    /// </summary>
    /// <param name="record">兩翼厚度與 A／低翼對應。</param>
    /// <param name="ct">取消權杖。</param>
    Task UpsertAsync(JobThicknessRecord record, CancellationToken ct = default);
}
