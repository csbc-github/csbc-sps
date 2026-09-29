namespace ThreeDScan.Domain.Models;

/// <summary>操作稽核紀錄（對應 OperationLog 表）。</summary>
public sealed class OperationLogEntry
{
    /// <summary>紀錄時刻。</summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.Now;

    /// <summary>追溯 ID。</summary>
    public required string TraceId { get; init; }

    /// <summary>呼叫端識別。</summary>
    public required string ClientId { get; init; }

    /// <summary>操作者。</summary>
    public string? Operator { get; init; }

    /// <summary>動作名稱。</summary>
    public required string Action { get; init; }

    /// <summary>操作對象（例如 scannerId）。</summary>
    public string? Target { get; init; }

    /// <summary>Success 或 Fail。</summary>
    public required string Result { get; init; }

    /// <summary>失敗時的錯誤碼。</summary>
    public string? ErrorCode { get; init; }

    /// <summary>補充說明。</summary>
    public string? Detail { get; init; }
}
