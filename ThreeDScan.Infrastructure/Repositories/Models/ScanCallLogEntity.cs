namespace ThreeDScan.Infrastructure.Repositories.Models;

/// <summary>對應資料表 <c>ScanCallLog</c>：掃描 API 呼叫紀錄。</summary>
public sealed class ScanCallLogEntity
{
    /// <summary>主鍵。</summary>
    public long Id { get; set; }

    /// <summary>呼叫時刻。</summary>
    public DateTimeOffset Timestamp { get; set; }

    /// <summary>追溯 ID。</summary>
    public string TraceId { get; set; } = "";

    /// <summary>動作名稱。</summary>
    public string Action { get; set; } = "";

    /// <summary>掃描器編號。</summary>
    public string? ScannerId { get; set; }

    /// <summary>工單號。</summary>
    public string? JobId { get; set; }

    /// <summary>任務識別碼。</summary>
    public string? TaskId { get; set; }

    /// <summary>段落序號。</summary>
    public int? Seq { get; set; }

    /// <summary>是否成功。</summary>
    public bool Success { get; set; }

    /// <summary>失敗時的錯誤碼。</summary>
    public string? ErrorCode { get; set; }

    /// <summary>失敗時的說明。</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>請求 JSON。</summary>
    public string? RequestJson { get; set; }

    /// <summary>回應 JSON。</summary>
    public string? ResponseJson { get; set; }

    /// <summary>點雲檔路徑。</summary>
    public string? PointCloudPath { get; set; }

    /// <summary>點雲 SHA-256。</summary>
    public string? PointCloudSha256 { get; set; }

    /// <summary>ZIP 路徑。</summary>
    public string? ZipPath { get; set; }

    /// <summary>ZIP SHA-256。</summary>
    public string? ZipSha256 { get; set; }
}
