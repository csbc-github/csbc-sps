using ThreeDScan.Domain.Models;

namespace ThreeDScan.Domain.Abstractions;

/// <summary>線掃描任務暫存（ExportRoot/line-tasks）。</summary>
public interface ILineScanTaskService
{
    /// <summary>建立尚未錄製的任務。</summary>
    LineScanTaskRecord Create(string scannerId, double rollerSpeed, string traceId);

    /// <summary>依 taskId 讀取；不存在回傳 false。</summary>
    bool TryGet(string taskId, out LineScanTaskRecord? record);

    /// <summary>同一光達是否已有尚未結束的任務。</summary>
    bool TryGetActiveByScanner(string scannerId, out LineScanTaskRecord? record);

    /// <summary>寫入記憶體與 task.json。</summary>
    void Save(LineScanTaskRecord record);

    /// <summary>刪除任務目錄與快取。逾時清除用。</summary>
    void Remove(string taskId);

    /// <summary>尚未結束的任務。</summary>
    IReadOnlyList<LineScanTaskRecord> ListIncomplete();
}
