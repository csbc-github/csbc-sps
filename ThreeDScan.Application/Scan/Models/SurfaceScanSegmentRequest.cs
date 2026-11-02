using System.ComponentModel.DataAnnotations;
using ThreeDScan.Domain.Models;

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

    /// <summary>規格 v1.1 未列，由 Hank 搬入。掃描器座標 ROI。</summary>
    public ScannerRoi? Roi { get; set; }

    /// <summary>規格 v1.1 未列，由 Hank 搬入。候選物件 rank。</summary>
    public int? ComponentRank { get; set; }

    /// <summary>規格 v1.1 未列，由 Hank 搬入。段鍵，供冪等。</summary>
    public string? SegmentKey { get; set; }
}
