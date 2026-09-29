using System.ComponentModel.DataAnnotations;

namespace ThreeDScan.Application.Scan.Models;

/// <summary>POST /api/scan/surface/abort 請求。</summary>
public sealed class SurfaceScanAbortRequest
{
    /// <summary>/start 回傳之任務識別碼。</summary>
    [Required, StringLength(64, MinimumLength = 1)]
    public string TaskId { get; set; } = "";
}
