using ThreeDScan.Domain.Models;

namespace ThreeDScan.Application.Scan;

/// <summary>面／孔背景工作佇列。對應 Hank <c>Scan3D.Service.WorkQueue</c>。</summary>
public interface IScanWorkQueue
{
    /// <summary>該任務的取消權杖。</summary>
    /// <remarks>來自 Hank Scan3D 的 <c>Scan3D.Service.WorkQueue</c>。</remarks>
    /// <param name="taskId">任務編號。</param>
    CancellationToken Token(string taskId);

    /// <summary>取消該任務。</summary>
    /// <remarks>來自 Hank Scan3D 的 <c>Scan3D.Service.WorkQueue</c>。</remarks>
    /// <param name="taskId">任務編號。</param>
    void Cancel(string taskId);

    /// <summary>工作結束後移除取消來源。</summary>
    /// <remarks>來自 Hank Scan3D 的 <c>Scan3D.Service.WorkQueue</c>。</remarks>
    /// <param name="taskId">任務編號。</param>
    void Finished(string taskId);

    /// <summary>排入工作；佇列滿則丟 QUEUE_FULL。</summary>
    /// <remarks>來自 Hank Scan3D 的 <c>Scan3D.Service.WorkQueue</c>。</remarks>
    /// <param name="item">工作。</param>
    void Enqueue(ScanWorkItem item);

    /// <summary>讀取佇列。</summary>
    /// <remarks>來自 Hank Scan3D 的 <c>Scan3D.Service.WorkQueue</c>。</remarks>
    /// <param name="ct">取消權杖。</param>
    IAsyncEnumerable<ScanWorkItem> ReadAllAsync(CancellationToken ct);
}
