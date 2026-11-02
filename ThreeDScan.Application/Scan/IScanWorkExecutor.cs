using ThreeDScan.Domain.Models;

namespace ThreeDScan.Application.Scan;

/// <summary>背景工作執行。對應 Hank <c>ScanOrchestrator.ExecuteAsync</c>。</summary>
public interface IScanWorkExecutor
{
    /// <summary>執行一項背景工作。</summary>
    /// <remarks>來自 Hank Scan3D 的 <c>Scan3D.Service.ScanOrchestrator</c>。</remarks>
    /// <param name="item">工作。</param>
    /// <param name="ct">取消權杖（含任務取消）。</param>
    Task ExecuteAsync(ScanWorkItem item, CancellationToken ct);

    /// <summary>標記中斷。</summary>
    /// <remarks>來自 Hank Scan3D 的 <c>Scan3D.Service.ScanOrchestrator</c>。</remarks>
    /// <param name="taskId">任務編號。</param>
    /// <param name="shuttingDown">是否因行程關閉。</param>
    Task MarkInterruptedAsync(string taskId, bool shuttingDown);

    /// <summary>標記失敗。</summary>
    /// <remarks>來自 Hank Scan3D 的 <c>Scan3D.Service.ScanOrchestrator</c>。</remarks>
    /// <param name="taskId">任務編號。</param>
    /// <param name="error">例外。</param>
    Task MarkFailedAsync(string taskId, Exception error);

    /// <summary>釋放掃描器佔用。</summary>
    /// <remarks>來自 Hank Scan3D 的 <c>Scan3D.Service.ScanOrchestrator</c>。</remarks>
    /// <param name="taskId">任務編號。</param>
    void ReleaseDevice(string taskId);
}
