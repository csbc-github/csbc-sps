namespace ThreeDScan.Infrastructure.Configuration;

/// <summary>單台掃描器 appsettings 連線設定（綁定 Scanners 區段）。</summary>
public sealed class ScannerEndpointOptions
{
    public string Ip { get; set; } = "192.168.0.1";
    public ushort Port { get; set; } = 24691;
    public ushort HighSpeedPort { get; set; } = 24692;
    public int? NavigatorScanNo { get; set; }
    public string MachineArea { get; set; } = "printing";
}
