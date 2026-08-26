namespace ThreeDScan.Domain.Models;

/// <summary>
/// 掃描 API 一次呼叫紀錄（只追加、不修改）。
/// 含當時 request／response 與回傳檔 SHA256。
/// </summary>
public sealed class ScanCallLogRecord
{
    /// <summary>寫入時間。</summary>
    public DateTimeOffset Timestamp { get; init; }

    /// <summary>與 API <c>traceId</c> 相同。</summary>
    public string TraceId { get; init; } = "";

    /// <summary>見 <see cref="ScanCallLogActions"/>。</summary>
    public string Action { get; init; } = "";

    /// <summary>掃描器編號；附件下載可能為空。</summary>
    public string? ScannerId { get; init; }

    /// <summary>工單號；線掃描／附件下載可能為空。</summary>
    public string? JobId { get; init; }

    /// <summary>面掃描任務識別碼。</summary>
    public string? TaskId { get; init; }

    /// <summary>面掃描 seq；非面掃描為 null。</summary>
    public int? Seq { get; init; }

    /// <summary>本次呼叫是否成功。</summary>
    public bool Success { get; init; }

    /// <summary>失敗時的錯誤碼，例如 <c>SCAN_THK_002</c>、<c>SYS_NYI</c>。</summary>
    public string? ErrorCode { get; init; }

    /// <summary>失敗時回給呼叫端的訊息。</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>當次 request JSON（不含 Token）。</summary>
    public string? RequestJson { get; init; }

    /// <summary>當次回傳 JSON（成功為 envelope；附件成功則記檔名與路徑）。</summary>
    public string? ResponseJson { get; init; }

    /// <summary>回傳點雲的本機絕對路徑。</summary>
    public string? PointCloudPath { get; init; }

    /// <summary>點雲檔 SHA256（hex）；沒有檔則 null。</summary>
    public string? PointCloudSha256 { get; init; }

    /// <summary>回傳 ZIP 的本機絕對路徑。</summary>
    public string? ZipPath { get; init; }

    /// <summary>ZIP 檔 SHA256（hex）；沒有檔則 null。</summary>
    public string? ZipSha256 { get; init; }
}
