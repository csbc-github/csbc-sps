using System.ComponentModel.DataAnnotations;
using ThreeDScan.Application.Common.Models;

namespace ThreeDScan.Application.Calibration.Models;

/// <summary>POST /api/calibration/instrument/complete 請求。</summary>
public sealed class InstrumentCalibrationCompleteRequest
{
    /// <summary>/start 回傳之任務識別碼。</summary>
    [Required, StringLength(64, MinimumLength = 1)]
    public string TaskId { get; set; } = "";

    /// <summary>第二次拍攝時台船預期掃描器位置。</summary>
    [Required]
    public CoordinateDto CameraExpectedPosition { get; set; } = new();

    /// <summary>第二次拍攝之標準球已知絕對座標。</summary>
    [Required, MinLength(1), MaxLength(10)]
    public List<StandardBallDto> StandardBalls { get; set; } = new();

    /// <summary>拍攝群組 Scan 編號（1～16）；每次拍攝可各自指定。</summary>
    [Range(1, 16)]
    public int? NavigatorScanNo { get; set; }

    /// <summary>結果回呼網址。</summary>
    [Required, StringLength(2048, MinimumLength = 1)]
    public string CallbackUrl { get; set; } = "";
}
