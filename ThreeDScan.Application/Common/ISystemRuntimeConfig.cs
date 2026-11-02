namespace ThreeDScan.Application.Common;

/// <summary>啟動時自 SystemConfig 載入的執行期設定（R02）。</summary>
public interface ISystemRuntimeConfig
{
    /// <summary>ScanWorkQueue 容量上限。</summary>
    int MaximumPendingTasks { get; }

    /// <summary>ScanTask 閒置逾時（分鐘）。</summary>
    int WatchdogMinutes { get; }

    /// <summary>Callback 允許主機（自 SystemConfig 解析）。</summary>
    IReadOnlyList<string> CallbackAllowedHosts { get; }
}
