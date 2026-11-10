using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ThreeDScan.Domain.Abstractions;
using ThreeDScan.Domain.Models;
using ThreeDScan.Infrastructure;

namespace ThreeDScan;

internal static class WpfServiceProviderFactory
{
    private static IServiceProvider? _provider;

    public static IServiceProvider Get(IConfiguration config)
    {
        if (_provider is not null) return _provider;

        var services = new ServiceCollection();
        services.AddThreeDScanInfrastructure(config);
        _provider = services.BuildServiceProvider();
        return _provider;
    }

    public static IServiceScope CreateScope(IConfiguration config)
    {
        return Get(config).CreateScope();
    }

    public static IScannerRegistry GetScannerRegistry(IConfiguration config)
    {
        return Get(config).GetRequiredService<IScannerRegistry>();
    }

    /// <summary>依 appsettings「LineScanners」解析進料區本機即時預覽用的光達連線。</summary>
    /// <param name="config">WPF appsettings。</param>
    /// <param name="endpoint">解析成功時的 IP／埠與 scannerId。</param>
    /// <returns>scannerId 存在且 Ip 有效時為 true。</returns>
    public static bool TryResolveLineScannerEndpoint(IConfiguration config, out LineScannerEndpoint endpoint)
    {
        endpoint = null!;
        var scannerId = config["LineScan:DefaultScannerId"];
        if (string.IsNullOrWhiteSpace(scannerId))
        {
            foreach (var child in config.GetSection("LineScanners").GetChildren())
            {
                if (string.IsNullOrWhiteSpace(child.Key))
                {
                    continue;
                }

                scannerId = child.Key;
                break;
            }
        }

        if (string.IsNullOrWhiteSpace(scannerId))
        {
            return false;
        }

        var registry = Get(config).GetRequiredService<ILineScannerRegistry>();
        return registry.TryGet(scannerId.Trim(), out endpoint);
    }
}
