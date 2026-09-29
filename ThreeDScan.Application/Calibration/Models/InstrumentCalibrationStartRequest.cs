using System.ComponentModel.DataAnnotations;
using ThreeDScan.Application.Common.Models;

namespace ThreeDScan.Application.Calibration.Models;

/// <summary>POST /api/calibration/instrument/start 請求。</summary>
public sealed class InstrumentCalibrationStartRequest
{
    /// <summary>掃描器編號。</summary>
    [Required, StringLength(50, MinimumLength = 1)]
    public string ScannerId { get; set; } = "";

    /// <summary>台船預期掃描器位置。</summary>
    [Required]
    public CoordinateDto CameraExpectedPosition { get; set; } = new();

    /// <summary>標準球已知絕對座標。</summary>
    [Required, MinLength(1), MaxLength(10)]
    public List<StandardBallDto> StandardBalls { get; set; } = new();

    /// <summary>拍攝群組 Scan 編號（1～16）；省略時用掃描器預設。</summary>
    [Range(1, 16)]
    public int? NavigatorScanNo { get; set; }

    /// <summary>操作者（選填，寫入稽核）。</summary>
    public string? Operator { get; set; }
}
