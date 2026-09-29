namespace ThreeDScan.Infrastructure.Configuration;

/// <summary>單台掃描器 appsettings 連線設定（綁定 Scanners 區段）。</summary>
public sealed class ScannerEndpointOptions
{
    /// <summary>IP 位址。</summary>
    public string Ip { get; set; } = "192.168.0.1";

    /// <summary>通訊埠。</summary>
    public ushort Port { get; set; } = 24691;

    /// <summary>高速埠。</summary>
    public ushort HighSpeedPort { get; set; } = 24692;

    /// <summary>預設 Navigator Scan 編號（1～16）。</summary>
    public int? NavigatorScanNo { get; set; }

    /// <summary>產線區域（printing／cutting）。</summary>
    public string MachineArea { get; set; } = "printing";

    /// <summary>
    /// 有值時不連掃描器，改讀該資料夾的 capture_height.png／capture_gray.png。
    /// 空白則走現場 LJ 擷取。
    /// </summary>
    public string OfflineCaptureDirectory { get; set; } = "";
}
