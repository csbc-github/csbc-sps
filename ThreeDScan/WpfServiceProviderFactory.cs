using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ThreeDScan.Domain.Abstractions;
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
}
