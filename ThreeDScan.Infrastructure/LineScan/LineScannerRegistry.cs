using Microsoft.Extensions.Options;
using ThreeDScan.Domain.Abstractions;
using ThreeDScan.Domain.Models;
using ThreeDScan.Infrastructure.Configuration;

namespace ThreeDScan.Infrastructure.LineScan;

/// <summary>依 appsettings「LineScanners」解析光達連線。</summary>
public sealed class LineScannerRegistry : ILineScannerRegistry
{
    private readonly LineScannerRegistryOptions _options;

    /// <summary>以 LineScanners 設定建立註冊表。</summary>
    public LineScannerRegistry(IOptions<LineScannerRegistryOptions> options)
    {
        _options = options.Value;
    }

    /// <inheritdoc />
    public bool TryGet(string scannerId, out LineScannerEndpoint endpoint)
    {
        if (string.IsNullOrWhiteSpace(scannerId) || !_options.TryGetValue(scannerId.Trim(), out var opt)
            || string.IsNullOrWhiteSpace(opt.Ip))
        {
            endpoint = null!;
            return false;
        }

        endpoint = new LineScannerEndpoint
        {
            ScannerId = scannerId.Trim(),
            Ip = opt.Ip.Trim(),
            Port = opt.Port,
        };
        return true;
    }
}
