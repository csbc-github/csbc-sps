using System.ComponentModel.DataAnnotations;

namespace ThreeDScan.Application.Scan.Dtos;

/// <summary>線掃描請求。</summary>
public sealed class LineScanRequest
{
    /// <summary>光達感測器編號。</summary>
    [Required, StringLength(50, MinimumLength = 1)]
    public string ScannerId { get; set; } = "";

    /// <summary>滾輪台移動速度 (mm/s)，用以推算強制結束時間。</summary>
    [Range(0.001, 10000)]
    public double RollerSpeed { get; set; }
}
