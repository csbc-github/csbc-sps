namespace ThreeDScan.Domain.Models;

/// <summary>掃描器連線端點（由 appsettings Scanners 註冊表解析）。</summary>
public sealed class ScannerEndpoint
{
    /// <summary>掃描器編號。</summary>
    public required string ScannerId { get; init; }

    /// <summary>IP 位址。</summary>
    public required string Ip { get; init; }

    /// <summary>通訊埠。</summary>
    public ushort Port { get; init; } = 24691;

    /// <summary>高速埠。</summary>
    public ushort HighSpeedPort { get; init; } = 24692;

    /// <summary>預設 Navigator Scan 編號（1～16）。</summary>
    public int? NavigatorScanNo { get; init; }

    /// <summary>產線區域（printing／cutting）。</summary>
    public string MachineArea { get; init; } = "printing";

    /// <summary>
    /// 略過掃描器時讀取的擷取資料夾（內含 raw_scene.ply）。空白表示未設定。
    /// </summary>
    public string OfflineCaptureDirectory { get; init; } = "";
}
