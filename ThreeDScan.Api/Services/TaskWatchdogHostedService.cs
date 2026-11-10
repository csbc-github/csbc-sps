using ThreeDScan.Application.Calibration;
using ThreeDScan.Application.Common;
using ThreeDScan.Application.Scan;
using ThreeDScan.Application.Scan.Models;
using ThreeDScan.Domain.Abstractions;
using ThreeDScan.Domain.Models;
using ThreeDScan.Domain.Repositories;

namespace ThreeDScan.Api.Services;

/// <summary>多階段任務逾時自動中斷（規格 4.2）。</summary>
public sealed class TaskWatchdogHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ISystemRuntimeConfig _runtime;
    private readonly ILogger<TaskWatchdogHostedService> _logger;

    /// <summary>建立 Watchdog 背景服務。</summary>
    public TaskWatchdogHostedService(
        IServiceScopeFactory scopes,
        ISystemRuntimeConfig runtime,
        ILogger<TaskWatchdogHostedService> logger)
    {
        _scopes = scopes;
        _runtime = runtime;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SweepAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Watchdog 巡檢失敗。");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>中斷逾時的面掃描、線掃描與儀器校正任務。</summary>
    /// <remarks>來自 Hank Scan3D 的 <c>Scan3D.Service.TaskWatchdog</c>。</remarks>
    private async Task SweepAsync(CancellationToken ct)
    {
        var minutes = _runtime.WatchdogMinutes;
        if (minutes <= 0)
        {
            return;
        }

        var cutoff = DateTimeOffset.Now.AddMinutes(-minutes);
        using var scope = _scopes.CreateScope();
        var scan = scope.ServiceProvider.GetRequiredService<IScanAppService>();
        var tasks = scope.ServiceProvider.GetRequiredService<ISurfaceScanTaskService>();
        foreach (var task in tasks.ListIncomplete())
        {
            var started = task.StartedAt == default ? task.UpdatedAt : task.StartedAt;
            if (started > cutoff)
            {
                continue;
            }

            _logger.LogInformation("Watchdog 中斷面掃描 taskId={TaskId}。", task.TaskId);
            await scan.AbortSurfaceScanAsync(
                new SurfaceScanAbortRequest { TaskId = task.TaskId },
                ct).ConfigureAwait(false);
        }

        var scanTasks = scope.ServiceProvider.GetRequiredService<IScanTaskRepository>();
        foreach (var taskId in await scanTasks.NonTerminalTaskIdsAsync(ct).ConfigureAwait(false))
        {
            var lineDoc = await scanTasks.FindAsync(taskId, ct).ConfigureAwait(false);
            if (lineDoc is null || lineDoc.Kind != "Line" || lineDoc.Terminal)
            {
                continue;
            }

            var started = lineDoc.CreatedAt;
            if (started > cutoff)
            {
                continue;
            }

            _logger.LogInformation("Watchdog 中斷線掃描 taskId={TaskId}。", taskId);
            await scan.ExpireLineScanAsync(taskId, ct).ConfigureAwait(false);
        }

        var workQueue = scope.ServiceProvider.GetRequiredService<IScanWorkQueue>();
        var workExecutor = scope.ServiceProvider.GetRequiredService<IScanWorkExecutor>();
        var scanTaskIds = await scanTasks.TaskIdsAsync(ct).ConfigureAwait(false);
        foreach (var id in scanTaskIds)
        {
            try
            {
                var doc = await scanTasks.GetAsync(id, ct: ct).ConfigureAwait(false);
                if (doc.Terminal || doc.UpdatedAt > DateTimeOffset.UtcNow.AddMinutes(-minutes))
                {
                    continue;
                }

                _logger.LogInformation("Watchdog 中斷 ScanTask taskId={TaskId} kind={Kind}。", doc.TaskId, doc.Kind);
                try
                {
                    await workExecutor.MarkFailedAsync(
                        id,
                        new ScanTaskException(408, "TASK_WATCHDOG_TIMEOUT", "任務逾時，請重新開始。")).ConfigureAwait(false);
                }
                catch (Exception markEx)
                {
                    _logger.LogWarning(markEx, "Watchdog MarkFailed 失敗 taskId={TaskId}，仍取消背景工作。", id);
                }

                workQueue.Cancel(id);
                if (doc.Phase is "Collecting" or "AwaitingNextShot" or "LineAcquiring" or "HoleCapture")
                {
                    workExecutor.ReleaseDevice(id);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Watchdog failed for ScanTask {TaskId}", id);
            }
        }

        var calibration = scope.ServiceProvider.GetRequiredService<ICalibrationAppService>();
        var instrumentTasks = scope.ServiceProvider.GetRequiredService<IInstrumentCalibrationTaskRepository>();
        var pending = await instrumentTasks.ListPendingAsync(ct).ConfigureAwait(false);
        foreach (var task in pending)
        {
            if (task.CreatedAt > cutoff)
            {
                continue;
            }

            _logger.LogInformation("Watchdog 逾時儀器校正 taskId={TaskId}。", task.TaskId);
            await calibration.ExpireInstrumentTaskAsync(task.TaskId, ct).ConfigureAwait(false);
        }
    }
}
