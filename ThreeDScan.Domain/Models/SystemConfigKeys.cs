namespace ThreeDScan.Domain.Models;

/// <summary>SystemConfig 資料表鍵名與啟動缺省值（R02）。</summary>
public static class SystemConfigKeys
{
    /// <summary>背景 ScanTask 佇列容量上限。</summary>
    public const string MaximumPendingTasks = "MaximumPendingTasks";

    /// <summary>ScanTask 閒置逾時（分鐘；Hank WatchdogMinutes）。</summary>
    public const string WatchdogMinutes = "WatchdogMinutes";

    /// <summary>Callback 允許主機（分號或逗號分隔）。</summary>
    public const string CallbackAllowedHosts = "CallbackAllowedHosts";

    /// <summary>MaximumPendingTasks 缺省值。</summary>
    public const int DefaultMaximumPendingTasks = 64;

    /// <summary>WatchdogMinutes 缺省值。</summary>
    public const int DefaultWatchdogMinutes = 10;

    /// <summary>CallbackAllowedHosts 缺省值。</summary>
    public const string DefaultCallbackAllowedHosts = "127.0.0.1;localhost";
}
