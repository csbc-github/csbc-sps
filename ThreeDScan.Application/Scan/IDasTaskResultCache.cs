using ThreeDScan.Application.Scan.Models;

namespace ThreeDScan.Application.Scan;

/// <summary>指南任務結果暫存（供 GET /api/tasks/{taskId}/result）。</summary>
public interface IDasTaskResultCache
{
    /// <summary>寫入或覆蓋任務結果。</summary>
    void Set(TaskResultData data);

    /// <summary>依 taskId 讀取；不存在回傳 false。</summary>
    bool TryGet(string taskId, out TaskResultData? data);
}
