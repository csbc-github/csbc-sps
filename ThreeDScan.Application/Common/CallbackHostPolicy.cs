namespace ThreeDScan.Application.Common;

/// <summary>Callback 主機白名單（ValidateCallback 與 ScanCallbackWorker 共用，R07）。</summary>
public static class CallbackHostPolicy
{
    /// <summary>
    /// Hank：CallbackMode 為 Enabled 時，主機須在 SystemConfig 白名單內。
    /// </summary>
    /// <param name="callbackMode">appsettings Callback:Mode。</param>
    /// <param name="host">URL 主機。</param>
    /// <param name="allowedHosts">SystemConfig 允許主機。</param>
    /// <returns>是否允許。</returns>
    public static bool IsHostAllowed(string callbackMode, string host, IReadOnlyList<string> allowedHosts)
    {
        if (!callbackMode.Equals("Enabled", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (allowedHosts.Count == 0)
        {
            return false;
        }

        return allowedHosts.Contains(host, StringComparer.OrdinalIgnoreCase);
    }
}
