namespace ThreeDScan.Domain.Models;

/// <summary>掃描器連線端點（由 appsettings Scanners 註冊表解析）。</summary>
public sealed class ScannerEndpoint
{
    public required string ScannerId { get; init; }
    public required string Ip { get; init; }
    public ushort Port { get; init; } = 24691;
    public ushort HighSpeedPort { get; init; } = 24692;
    public int? NavigatorScanNo { get; init; }
    public string MachineArea { get; init; } = "printing";
}
