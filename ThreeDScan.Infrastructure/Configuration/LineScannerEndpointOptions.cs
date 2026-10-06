namespace ThreeDScan.Infrastructure.Configuration;

/// <summary>單台光達的 appsettings 連線（綁定 LineScanners）。</summary>
public sealed class LineScannerEndpointOptions
{
    /// <summary>光達 IP。</summary>
    public string Ip { get; set; } = "192.168.0.1";

    /// <summary>CoLa B 埠。</summary>
    public int Port { get; set; } = 2111;
}
