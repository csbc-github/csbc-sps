using System.ComponentModel.DataAnnotations;

namespace ThreeDScan.Application.Scan.Models;

/// <summary>POST /api/scan/surface/segment 請求。</summary>
public sealed class SurfaceScanSegmentRequest
{
    /// <summary>/start 回傳之任務識別碼。</summary>
    [Required, StringLength(64, MinimumLength = 1)]
    public string TaskId { get; set; } = "";

    /// <summary>本次拍攝時掃描器絕對位置與姿態。</summary>
    [Required]
    public CameraPoseDto? CameraPosition { get; set; }

    /// <summary>拍攝群組 Scan 編號（1～16）；每次拍攝可各自指定。</summary>
    [Range(1, 16)]
    public int? NavigatorScanNo { get; set; }
}
