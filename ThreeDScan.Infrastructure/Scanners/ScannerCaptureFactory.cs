using Microsoft.Extensions.Options;
using ThreeDScan.Domain.Abstractions;
using ThreeDScan.Infrastructure.Configuration;
using ThreeDScan.Infrastructure.Export;

namespace ThreeDScan.Infrastructure.Scanners;

/// <summary>依 appsettings 建立各掃描器的 LJS8 擷取服務實例。</summary>
public sealed class ScannerCaptureFactory : IScannerCaptureFactory
{
    private readonly Dictionary<string, Ljs8SurfaceScannerCapture> _captures;

    public ScannerCaptureFactory(
        IOptions<ScannerRegistryOptions> options,
        IOptions<StorageOptions> storageOptions,
        ISurfaceScanPipeline pipeline)
    {
        var exportRoot = CaptureExportPath.ResolveRoot(storageOptions.Value.ExportRoot);
        Directory.CreateDirectory(exportRoot);

        _captures = options.Value.ToDictionary(
            kv => kv.Key,
            kv => new Ljs8SurfaceScannerCapture(kv.Key, kv.Value, exportRoot, pipeline),
            StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public ISurfaceScannerCapture GetCapture(string scannerId)
    {
        if (_captures.TryGetValue(scannerId, out var capture))
            return capture;
        throw new InvalidOperationException($"未知 ScannerId: {scannerId}");
    }
}
