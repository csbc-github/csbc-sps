using ThreeDScan.Domain.Abstractions;
using ThreeDScan.Domain.Models;
using ThreeDScan.Domain.Repositories;

namespace ThreeDScan.Application.Scan;

/// <summary>
/// 背景工作執行。面／孔正式路徑尚未接上時，未知 Operation 記 Failed。
/// </summary>
/// <remarks>對應 Hank <c>ScanOrchestrator.ExecuteAsync／MarkFailedAsync／MarkInterruptedAsync</c>。</remarks>
public sealed class ScanWorkExecutor : IScanWorkExecutor
{
    private readonly IScanTaskRepository _tasks;
    private readonly IDeviceCoordinator _devices;
    private readonly IScanOrchestrator _orchestrator;

    /// <summary>建立執行器。</summary>
    /// <param name="tasks">任務儲存。</param>
    /// <param name="devices">掃描器互斥。</param>
    /// <param name="orchestrator">面／孔正式路徑。</param>
    public ScanWorkExecutor(
        IScanTaskRepository tasks,
        IDeviceCoordinator devices,
        IScanOrchestrator orchestrator)
    {
        _tasks = tasks;
        _devices = devices;
        _orchestrator = orchestrator;
    }

    /// <inheritdoc />
    public Task ExecuteAsync(ScanWorkItem item, CancellationToken ct)
    {
        if (item.Operation == "SurfaceComplete")
        {
            return _orchestrator.ExecuteSurfaceAsync(item.TaskId, ct);
        }

        if (item.Operation == "Hole")
        {
            return _orchestrator.ExecuteHoleAsync(item.TaskId, ct);
        }

        throw new ScanTaskException(400, "SYS_NYI", "未知背景操作：" + item.Operation);
    }

    /// <inheritdoc />
    public async Task MarkInterruptedAsync(string taskId, bool shuttingDown)
    {
        var existing = await _tasks.FindAsync(taskId).ConfigureAwait(false);
        if (existing is null || existing.Terminal)
        {
            return;
        }

        await _tasks.UpdateAsync(taskId, doc =>
        {
            if (doc.Terminal)
            {
                return;
            }

            doc.Artifacts.Clear();
            doc.Result = null;
            if (shuttingDown)
            {
                doc.Status = "Failed";
                doc.Phase = "Failed";
                doc.Error = new TaskApiError
                {
                    Code = "SERVICE_STOPPED",
                    Message = "服務停止造成工作中斷；請重新發起掃描。",
                };
            }
            else
            {
                doc.Status = "Aborted";
                doc.Phase = "Aborted";
                doc.Error = null;
            }
        }).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task MarkFailedAsync(string taskId, Exception error)
    {
        var code = "SYS_INTERNAL";
        var message = error.Message;
        if (error is ScanTaskException scan)
        {
            code = scan.Code;
            message = scan.Message;
        }

        var existing = await _tasks.FindAsync(taskId).ConfigureAwait(false);
        if (existing is null || existing.Terminal)
        {
            return;
        }

        await _tasks.UpdateAsync(taskId, doc =>
        {
            if (doc.Terminal)
            {
                return;
            }

            doc.Status = "Failed";
            doc.Phase = "Failed";
            doc.Result = null;
            doc.Error = new TaskApiError
            {
                Code = code,
                Message = message,
            };
        }).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void ReleaseDevice(string taskId)
    {
        _devices.ReleaseByTaskId(taskId);
    }
}
