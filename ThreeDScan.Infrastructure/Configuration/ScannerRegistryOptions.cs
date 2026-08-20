namespace ThreeDScan.Infrastructure.Configuration;

/// <summary>Bound from appsettings "Scanners" section (scannerId → endpoint).</summary>
public sealed class ScannerRegistryOptions : Dictionary<string, ScannerEndpointOptions>
{
}
