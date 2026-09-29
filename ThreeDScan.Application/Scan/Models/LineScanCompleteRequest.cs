using System.ComponentModel.DataAnnotations;

namespace ThreeDScan.Application.Scan.Models;

/// <summary>POST /api/scan/line/complete 請求。</summary>
public sealed class LineScanCompleteRequest
{
    /// <summary>/start 回傳之任務識別碼。</summary>
    [Required, StringLength(64, MinimumLength = 1)]
    public string TaskId { get; set; } = "";

    /// <summary>結果回呼網址。</summary>
    [Required, StringLength(2048, MinimumLength = 1)]
    public string CallbackUrl { get; set; } = "";
}
