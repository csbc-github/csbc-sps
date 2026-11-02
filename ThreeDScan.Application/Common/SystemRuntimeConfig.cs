using ThreeDScan.Domain.Models;

namespace ThreeDScan.Application.Common;

/// <summary>SystemConfig 載入後的執行期設定快照。</summary>
public sealed class SystemRuntimeConfig : ISystemRuntimeConfig
{
    private int _maximumPendingTasks = SystemConfigKeys.DefaultMaximumPendingTasks;
    private int _watchdogMinutes = SystemConfigKeys.DefaultWatchdogMinutes;
    private string[] _callbackAllowedHosts = Array.Empty<string>();

    /// <inheritdoc />
    public int MaximumPendingTasks
    {
        get { return _maximumPendingTasks; }
    }

    /// <inheritdoc />
    public int WatchdogMinutes
    {
        get { return _watchdogMinutes; }
    }

    /// <inheritdoc />
    public IReadOnlyList<string> CallbackAllowedHosts
    {
        get { return _callbackAllowedHosts; }
    }

    /// <summary>自資料庫讀值更新快照（僅由啟動載入呼叫）。</summary>
    /// <param name="maximumPendingTasks">佇列容量。</param>
    /// <param name="watchdogMinutes">Watchdog 分鐘。</param>
    /// <param name="callbackAllowedHosts">允許主機。</param>
    public void Load(int maximumPendingTasks, int watchdogMinutes, string[] callbackAllowedHosts)
    {
        _maximumPendingTasks = maximumPendingTasks;
        _watchdogMinutes = watchdogMinutes;
        _callbackAllowedHosts = callbackAllowedHosts ?? Array.Empty<string>();
    }
}
