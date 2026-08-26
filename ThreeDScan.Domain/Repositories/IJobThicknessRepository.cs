using ThreeDScan.Domain.Models;

namespace ThreeDScan.Domain.Repositories;

/// <summary>
/// 工單兩翼厚度持久化：噴印量測後寫入，割切依 <c>jobId</c> 讀出組模型。
/// </summary>
public interface IJobThicknessRepository
{
    /// <summary>
    /// 依工單號讀取已存的噴印厚度。
    /// </summary>
    /// <param name="jobId">台船工單號；空白則視為找不到。</param>
    /// <param name="ct">取消權杖。</param>
    /// <returns>有紀錄則回傳該筆；尚無噴印量測則 <c>null</c>（呼叫端應回 <c>SCAN_THK_002</c>）。</returns>
    Task<JobThicknessRecord?> GetByJobIdAsync(string jobId, CancellationToken ct = default);

    /// <summary>
    /// 寫入或覆寫該工單的厚度。同一工單在噴印區重測時會更新，割切之後讀到的是最新一筆。
    /// </summary>
    /// <param name="record">要保存的兩翼厚度與 A／低翼對應。</param>
    /// <param name="ct">取消權杖。</param>
    Task UpsertAsync(JobThicknessRecord record, CancellationToken ct = default);
}
