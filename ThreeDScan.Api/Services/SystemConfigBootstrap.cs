using ThreeDScan.Application.Common;
using ThreeDScan.Domain.Models;
using ThreeDScan.Domain.Repositories;

namespace ThreeDScan.Api.Services;

/// <summary>啟動時補齊 SystemConfig 缺鍵並載入執行期設定（R02）。</summary>
public static class SystemConfigBootstrap
{
    /// <summary>確保三項缺省鍵存在（不覆寫既有值），並填入 <see cref="SystemRuntimeConfig"/>。</summary>
    /// <param name="services">應用程式服務。</param>
    /// <param name="ct">取消權杖。</param>
    public static async Task InitializeAsync(IServiceProvider services, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ISystemConfigRepository>();
        var runtime = scope.ServiceProvider.GetRequiredService<SystemRuntimeConfig>();

        await store.EnsureMissingAsync(
            SystemConfigKeys.MaximumPendingTasks,
            SystemConfigKeys.DefaultMaximumPendingTasks.ToString(),
            ct).ConfigureAwait(false);
        await store.EnsureMissingAsync(
            SystemConfigKeys.WatchdogMinutes,
            SystemConfigKeys.DefaultWatchdogMinutes.ToString(),
            ct).ConfigureAwait(false);
        await store.EnsureMissingAsync(
            SystemConfigKeys.CallbackAllowedHosts,
            SystemConfigKeys.DefaultCallbackAllowedHosts,
            ct).ConfigureAwait(false);

        var maxPendingRaw = await store.GetAsync(SystemConfigKeys.MaximumPendingTasks, ct).ConfigureAwait(false);
        var watchdogRaw = await store.GetAsync(SystemConfigKeys.WatchdogMinutes, ct).ConfigureAwait(false);
        var hostsRaw = await store.GetAsync(SystemConfigKeys.CallbackAllowedHosts, ct).ConfigureAwait(false);

        var maxPending = ParsePositiveInt(maxPendingRaw, SystemConfigKeys.DefaultMaximumPendingTasks);
        var watchdogMinutes = ParseNonNegativeInt(watchdogRaw, SystemConfigKeys.DefaultWatchdogMinutes);
        var hosts = CallbackAllowedHostsParser.Parse(hostsRaw);

        runtime.Load(maxPending, watchdogMinutes, hosts);
    }

    /// <summary>解析正整數；失敗時回傳缺省值。</summary>
    private static int ParsePositiveInt(string? raw, int defaultValue)
    {
        if (int.TryParse(raw, out var value) && value > 0)
        {
            return value;
        }

        return defaultValue;
    }

    /// <summary>解析非負整數；失敗時回傳缺省值。</summary>
    private static int ParseNonNegativeInt(string? raw, int defaultValue)
    {
        if (int.TryParse(raw, out var value) && value >= 0)
        {
            return value;
        }

        return defaultValue;
    }
}
