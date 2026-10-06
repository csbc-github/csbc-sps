namespace ThreeDScan.Domain.Models;

/// <summary>線掃描光達連線（appsettings LineScanners，與面掃描 Scanners 分開）。</summary>
public sealed class LineScannerEndpoint
{
    /// <summary>光達編號，對應線掃描與滾輪校正的 scannerId。</summary>
    public required string ScannerId { get; init; }

    /// <summary>光達 IP。</summary>
    public required string Ip { get; init; }

    /// <summary>CoLa B 埠。</summary>
    public int Port { get; init; } = 2111;
}
