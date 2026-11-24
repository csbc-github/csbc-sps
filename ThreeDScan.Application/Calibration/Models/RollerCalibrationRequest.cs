// 舊版 POST /api/calibration/roller 已停用；型別保留供對照。
using System.ComponentModel.DataAnnotations;

namespace ThreeDScan.Application.Calibration.Models;

/// <summary>滾輪校正請求：掃描滾輪台以取得 Z 距離。</summary>
public sealed class RollerCalibrationRequest
{
    /// <summary>光達感測器編號。</summary>
    [Required, StringLength(50, MinimumLength = 1)]
    public string ScannerId { get; set; } = "";
}
