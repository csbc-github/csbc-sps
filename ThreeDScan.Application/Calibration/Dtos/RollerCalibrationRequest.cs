using System.ComponentModel.DataAnnotations;

namespace ThreeDScan.Application.Calibration.Dtos;

/// <summary>滾輪校正請求：掃描滾輪台以取得 Z 距離。</summary>
public sealed class RollerCalibrationRequest
{
    /// <summary>光達感測器編號。</summary>
    [Required, StringLength(50, MinimumLength = 1)]
    public string ScannerId { get; set; } = "";
}
