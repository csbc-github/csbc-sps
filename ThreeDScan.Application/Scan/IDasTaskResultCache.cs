using ThreeDScan.Application.Scan.Models;

namespace ThreeDScan.Application.Scan;

/// <summary>指南任務結果暫存（供 GET /api/tasks/{taskId}/result）。</summary>
public interface IDasTaskResultCache
{
    /// <summary>寫入或覆蓋任務結果。</summary>
    void Set(TaskResultData data);

    /// <summary>依 taskId 讀取；不存在回傳 false。</summary>
    bool TryGet(string taskId, out TaskResultData? data);

    /// <summary>移除任務結果。逾時清除後，後續查詢視為不存在。</summary>
    void Remove(string taskId);

    /// <summary>記住最近一次線掃描 complete 的結果，供進料畫面讀取。</summary>
    void SetLatestLine(TaskResultData data);

    /// <summary>讀取最近一次線掃描結果；還沒有完成過則回傳 false。</summary>
    bool TryGetLatestLine(out TaskResultData? data);
}
