using ThreeDScan.Domain.Repositories;

namespace ThreeDScan.Api.Services;

/// <summary>啟動時恢復非終態 ScanTask（Hank <c>ScanOrchestrator.RecoverAsync</c>）。</summary>
public static class ScanTaskRecoveryBootstrap
{
    /// <summary>在背景 Worker 啟動前，將 DB 非終態任務標記為 SERVICE_RESTARTED。</summary>
    /// <param name="services">應用程式服務。</param>
    /// <param name="ct">取消權杖。</param>
    public static async Task RecoverAsync(IServiceProvider services, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IScanTaskRepository>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("ScanTaskRecovery");
        var count = await store.RecoverAfterServiceRestartAsync(ct).ConfigureAwait(false);
        if (count > 0)
        {
            logger.LogInformation("ScanTask 啟動恢復：已標記 {Count} 筆非終態任務為 Failed／SERVICE_RESTARTED。", count);
        }
    }
}
