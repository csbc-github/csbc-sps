using System.ComponentModel.DataAnnotations;
using ThreeDScan.Application.Common.Models;

namespace ThreeDScan.Application.Scan.Models;

/// <summary>POST /api/scan/surface/start 請求。</summary>
public sealed class SurfaceScanStartRequest
{
    /// <summary>掃描器編號（與工作區固定對應）。</summary>
    [Required, StringLength(50, MinimumLength = 1)]
    public string ScannerId { get; set; } = "";

    /// <summary>工單號；僅本端點需要，後續以 taskId 關聯。</summary>
    [Required, StringLength(64, MinimumLength = 1)]
    public string JobId { get; set; } = "";

    /// <summary>本次拍攝時掃描器絕對位置與姿態。</summary>
    [Required]
    public CameraPoseDto? CameraPosition { get; set; }

    /// <summary>拍攝群組 Scan 編號（1～16）；DAS 目前可不送。</summary>
    [Range(1, 16)]
    public int? NavigatorScanNo { get; set; }

    /// <summary>左斜面內表面座標（測距儀）；僅本端點可用。</summary>
    public CoordinateDto? InnerLeft { get; set; }

    /// <summary>右斜面內表面座標（測距儀）；僅本端點可用。</summary>
    public CoordinateDto? InnerRight { get; set; }
}
