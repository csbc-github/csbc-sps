using System.ComponentModel.DataAnnotations;

namespace ThreeDScan.Application.Calibration.Models;

/// <summary>POST /api/calibration/reference 請求。</summary>
public sealed class ReferenceSetupRequest
{
    /// <summary>掃描器編號。</summary>
    [Required, StringLength(50, MinimumLength = 1)]
    public string ScannerId { get; set; } = "";

    /// <summary>掃描時使用的拍攝群組 Scan 編號；省略時用 Scanner 預設。Ref 仍寫入該掃描器唯一一筆 ScannerConfig。</summary>
    public int? NavigatorScanNo { get; set; }

    /// <summary>操作者。</summary>
    public string? Operator { get; set; }
}
