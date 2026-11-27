using System.ComponentModel.DataAnnotations;
using ThreeDScan.Domain.Models;

namespace ThreeDScan.Application.Scan.Models;

/// <summary>孔洞掃描請求（指南 v1.1）。</summary>
public sealed class HoleScanRequest
{
    /// <summary>面掃描器編號。</summary>
    [Required, StringLength(50, MinimumLength = 1)]
    public string ScannerId { get; set; } = "";

    /// <summary>工單號（與噴印／割切區同一根角鋼保持一致）。</summary>
    [Required, StringLength(64, MinimumLength = 1)]
    public string JobId { get; set; } = "";

    /// <summary>特徵類型：Circle／Slot／EdgeNotch；省略時通用處理。</summary>
    [StringLength(32)]
    public string? HoleType { get; set; }

    /// <summary>結果回呼網址。</summary>
    [Required, StringLength(2048, MinimumLength = 1)]
    public string CallbackUrl { get; set; } = "";

    /// <summary>拍攝時掃描器絕對位置與姿態。必填，孔緣依此換算成實際座標。</summary>
    [Required]
    public CameraPoseDto? CameraPosition { get; set; }

    /// <summary>掃描器 XYZ 範圍；省略時辨識完整原始掃描，不自動套用舊站點 ROI。</summary>
    public ScannerRoi? Roi { get; set; }

    /// <summary>規格 v1.1 未列，由 Hank 搬入。指定特徵。</summary>
    public string? FeatureId { get; set; }

    /// <summary>拍攝群組 1～16；省略時沿用控制器目前程式，與 OneClick 相同。</summary>
    [Range(1, 16)]
    public int? NavigatorScanNo { get; set; }
}
