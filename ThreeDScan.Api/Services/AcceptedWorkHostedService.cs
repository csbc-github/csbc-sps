using ThreeDScan.Application.Common;

namespace ThreeDScan.Api.Services;

/// <summary>執行 complete 接受後的背景工作。</summary>
public sealed class AcceptedWorkHostedService : BackgroundService
{
    private readonly IAcceptedWorkQueue _queue;
    private readonly IServiceProvider _services;
    private readonly ILogger<AcceptedWorkHostedService> _logger;

    /// <summary>建立背景工作者。</summary>
    public AcceptedWorkHostedService(
        IAcceptedWorkQueue queue,
        IServiceProvider services,
        ILogger<AcceptedWorkHostedService> logger)
    {
        _queue = queue;
        _services = services;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            Func<IServiceProvider, CancellationToken, Task> work;
            try
            {
                work = await _queue.DequeueAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            try
            {
                await work(_services, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "已接受的背景工作失敗。");
            }
        }
    }
}
