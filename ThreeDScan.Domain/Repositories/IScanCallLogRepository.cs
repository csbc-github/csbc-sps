using ThreeDScan.Domain.Models;

namespace ThreeDScan.Domain.Repositories;

/// <summary>
/// 掃描 API 呼叫紀錄。僅追加。
/// </summary>
public interface IScanCallLogRepository
{
    /// <summary>
    /// 寫入一筆呼叫紀錄。不得更新或刪除既有列。
    /// </summary>
    /// <param name="record">當次 request／response 與檔案雜湊。</param>
    /// <param name="ct">取消權杖。</param>
    Task AppendAsync(ScanCallLogRecord record, CancellationToken ct = default);
}
