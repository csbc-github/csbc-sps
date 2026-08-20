using System.ComponentModel.DataAnnotations;

namespace ThreeDScan.Application.Scan.Dtos;

/// <summary>孔洞掃描請求。</summary>
public sealed class HoleScanRequest
{
    /// <summary>面掃描器編號。</summary>
    [Required, StringLength(50, MinimumLength = 1)]
    public string ScannerId { get; set; } = "";
}
