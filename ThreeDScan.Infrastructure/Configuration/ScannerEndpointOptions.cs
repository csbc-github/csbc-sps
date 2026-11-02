using ThreeDScan.Domain.Models;

namespace ThreeDScan.Infrastructure.Configuration;

/// <summary>單台掃描器 appsettings 連線與正式面掃描設定（綁定 Scanners 區段）。</summary>
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

    /// <summary>掃描器到絕對座標的基底（Hank <c>Scanners.*.Coordinates</c>）。</summary>
    public CoordinateTransformOptions Coordinates { get; set; } = new CoordinateTransformOptions();

    /// <summary>段抽取預設（Hank <c>Scanners.*.Processing</c>）。</summary>
    public SegmentProcessingOptions Processing { get; set; } = new SegmentProcessingOptions();

    /// <summary>工單未帶厚度時的預設板厚（mm）；未設定為 null。</summary>
    public double? MaterialThicknessMm { get; set; }

    /// <summary>拼接網格格距（mm）；未設定時為 1.0。</summary>
    public double MeshCellMm { get; set; } = 1.0;

    /// <summary>孔洞分析預設 ROI（Hank <c>Scanners.*.HoleRoi</c>）；未設定為 null。</summary>
    public ScannerRoi? HoleRoi { get; set; }

    /// <summary>孔洞掃描預設相機位姿（Hank <c>FixedCameraPosition</c>）；未設定為 null。</summary>
    public Pose5? FixedCameraPosition { get; set; }
}
