using System.ComponentModel.DataAnnotations;

namespace ThreeDScan.Application.Scan.Models;

/// <summary>線掃描開始請求（Encoder 位移；不需 rollerSpeed）。</summary>
public sealed class LineScanRequest
{
    /// <summary>光達感測器編號。</summary>
    [Required, StringLength(50, MinimumLength = 1)]
    public string ScannerId { get; set; } = "";
}
