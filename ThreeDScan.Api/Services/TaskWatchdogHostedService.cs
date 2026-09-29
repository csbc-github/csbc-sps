using Microsoft.Extensions.Options;
using ThreeDScan.Application.Calibration;
using ThreeDScan.Application.Common.Models;
using ThreeDScan.Application.Scan;
using ThreeDScan.Application.Scan.Models;
using ThreeDScan.Domain.Abstractions;
using ThreeDScan.Domain.Repositories;

namespace ThreeDScan.Api.Services;

/// <summary>多階段任務逾時自動中斷（規格 4.2）。</summary>
public sealed class TaskWatchdogHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly WatchdogSettings _settings;
    private readonly ILogger<TaskWatchdogHostedService> _logger;

    /// <summary>建立 Watchdog 背景服務。</summary>
    public TaskWatchdogHostedService(
        IServiceScopeFactory scopes,
        IOptions<WatchdogSettings> settings,
        ILogger<TaskWatchdogHostedService> logger)
    {
        _scopes = scopes;
        _settings = settings.Value;
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

    /// <summary>中斷逾時的面掃描與儀器校正任務（不重置硬體）。</summary>
    private async Task SweepAsync(CancellationToken ct)
    {
        var minutes = _settings.MaxTaskDurationMinutes;
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
