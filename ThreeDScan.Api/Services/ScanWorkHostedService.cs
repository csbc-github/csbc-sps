using ThreeDScan.Application.Scan;

namespace ThreeDScan.Api.Services;

/// <summary>讀取 <see cref="IScanWorkQueue"/> 並交給 <see cref="IScanWorkExecutor"/>。對應 Hank <c>ScanWorker</c>。</summary>
public sealed class ScanWorkHostedService : BackgroundService
{
    private readonly IScanWorkQueue _queue;
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<ScanWorkHostedService> _logger;

    /// <summary>建立背景工作者。</summary>
    public ScanWorkHostedService(
        IScanWorkQueue queue,
        IServiceScopeFactory scopes,
        ILogger<ScanWorkHostedService> logger)
    {
        _queue = queue;
        _scopes = scopes;
        _logger = logger;
    }

    /// <inheritdoc />
    /// <remarks>來自 Hank Scan3D 的 <c>Scan3D.Service.ScanWorker</c>。</remarks>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var item in _queue.ReadAllAsync(stoppingToken))
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, _queue.Token(item.TaskId));
            using var scope = _scopes.CreateScope();
            var executor = scope.ServiceProvider.GetRequiredService<IScanWorkExecutor>();
            try
            {
                await executor.ExecuteAsync(item, linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                await executor.MarkInterruptedAsync(item.TaskId, stoppingToken.IsCancellationRequested).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Background task {TaskId} failed", item.TaskId);
                await executor.MarkFailedAsync(item.TaskId, ex).ConfigureAwait(false);
            }
            finally
            {
                executor.ReleaseDevice(item.TaskId);
                _queue.Finished(item.TaskId);
            }
        }
    }
}
