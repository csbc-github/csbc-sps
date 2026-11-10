using Microsoft.Extensions.Configuration;

namespace ThreeDScan.Services;

/// <summary>依 appsettings「Scanners」的 MachineArea 解析 scannerId。</summary>
public static class MachineAreaScannerResolver
{
    /// <summary>取得指定產線區域的 scannerId。</summary>
    /// <param name="config">WPF appsettings。</param>
    /// <param name="machineArea">printing 或 cutting 等。</param>
    /// <param name="scannerId">解析成功時的鍵名。</param>
    /// <returns>找到對應 MachineArea 時為 true。</returns>
    public static bool TryResolve(IConfiguration config, string machineArea, out string scannerId)
    {
        scannerId = "";
        if (string.IsNullOrWhiteSpace(machineArea))
        {
            return false;
        }

        foreach (var child in config.GetSection("Scanners").GetChildren())
        {
            if (string.IsNullOrWhiteSpace(child.Key))
            {
                continue;
            }

            var area = child["MachineArea"];
            if (string.Equals(area, machineArea.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                scannerId = child.Key;
                return true;
            }
        }

        return false;
    }
}
