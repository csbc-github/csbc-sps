namespace ThreeDScan.Infrastructure.Repositories.Models;

/// <summary>對應資料表 OperationLog。</summary>
public sealed class OperationLogEntity
{
    /// <summary>主鍵。</summary>
    public long Id { get; set; }

    /// <summary>紀錄時刻。</summary>
    public DateTimeOffset Timestamp { get; set; }

    /// <summary>追溯 ID。</summary>
    public string TraceId { get; set; } = "";

    /// <summary>呼叫端識別。</summary>
    public string ClientId { get; set; } = "";

    /// <summary>操作者。</summary>
    public string? Operator { get; set; }

    /// <summary>動作名稱。</summary>
    public string Action { get; set; } = "";

    /// <summary>操作對象。</summary>
    public string? Target { get; set; }

    /// <summary>Success 或 Fail。</summary>
    public string Result { get; set; } = "";

    /// <summary>失敗時的錯誤碼。</summary>
    public string? ErrorCode { get; set; }

    /// <summary>補充說明。</summary>
    public string? Detail { get; set; }
}
