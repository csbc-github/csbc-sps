using Microsoft.Extensions.Options;
using ThreeDScan.Domain.Abstractions;
using ThreeDScan.Domain.Models;
using ThreeDScan.Infrastructure.Configuration;

namespace ThreeDScan.Infrastructure.Scanners;

/// <summary>依 appsettings「Scanners」解析掃描器端點與內容目錄路徑。</summary>
public sealed class ScannerRegistry : IScannerRegistry
{
    private readonly ScannerRegistryOptions _options;

    public ScannerRegistry(IOptions<ScannerRegistryOptions> options) => _options = options.Value;

    /// <inheritdoc />
    public bool TryGetEndpoint(string scannerId, out ScannerEndpoint endpoint)
    {
        if (!_options.TryGetValue(scannerId, out var opt))
        {
            endpoint = null!;
            return false;
        }

        endpoint = new ScannerEndpoint
        {
            ScannerId = scannerId,
            Ip = opt.Ip,
            Port = opt.Port,
            HighSpeedPort = opt.HighSpeedPort,
            NavigatorScanNo = opt.NavigatorScanNo,
            MachineArea = opt.MachineArea,
        };
        return true;
    }

    /// <inheritdoc />
    public byte ResolveProgramNo(string scannerId, int? navigatorScanNo)
    {
        if (navigatorScanNo.HasValue)
            return (byte)Math.Clamp(navigatorScanNo.Value - 1, 0, 15);

        if (_options.TryGetValue(scannerId, out var opt) && opt.NavigatorScanNo.HasValue)
            return (byte)Math.Clamp(opt.NavigatorScanNo.Value - 1, 0, 15);

        return 0;
    }

    /// <inheritdoc />
    public string ResolveScannerContentsFolder(string scannerId, string contentsRoot)
        => Path.Combine(contentsRoot, scannerId);

    /// <inheritdoc />
    public string ResolveContentsFolder(string scannerId, byte programNo, string contentsRoot)
    {
        var basePath = Path.Combine(contentsRoot, scannerId);
        return programNo == 0
            ? basePath
            : Path.Combine(basePath, $"program_{programNo}");
    }

    /// <summary>回傳 appsettings 中已註冊的全部 ScannerId。</summary>
    public IReadOnlyList<string> GetAllScannerIds() => _options.Keys.ToList();
}
