using System.ComponentModel.DataAnnotations;

namespace ThreeDScan.Application.Calibration.Dtos;

/// <summary>POST /api/calibration/instrument 請求（SD UC-01）。</summary>
public sealed class InstrumentCalibrationRequest
{
    /// <summary>面掃描器識別碼。</summary>
    [Required, StringLength(50, MinimumLength = 1)]
    public string ScannerId { get; set; } = "";

    /// <summary>拍攝群組 Scan 編號（1～16）；省略時用 Scanner 預設。不影響 Ref（ScannerConfig 每台一筆）。</summary>
    public int? NavigatorScanNo { get; set; }

    /// <summary>工作區域：grinding / printing / cutting。</summary>
    [Required]
    [RegularExpression(@"^(grinding|printing|cutting)$")]
    public string MachineArea { get; set; } = "printing";

    /// <summary>掃描器預期絕對位置（台船移動到位後的目標座標）。</summary>
    [Required]
    public CoordinateDto CameraExpectedPosition { get; set; } = new();

    /// <summary>標準球已知絕對座標；數量 1～10，以台船傳入為準。</summary>
    [Required, MinLength(1), MaxLength(10)]
    public List<StandardBallDto> StandardBalls { get; set; } = new();

    /// <summary>操作者（選填，寫入稽核）。</summary>
    public string? Operator { get; set; }
}
