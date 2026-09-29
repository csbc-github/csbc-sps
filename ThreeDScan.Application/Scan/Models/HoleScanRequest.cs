using System.ComponentModel.DataAnnotations;

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
}
