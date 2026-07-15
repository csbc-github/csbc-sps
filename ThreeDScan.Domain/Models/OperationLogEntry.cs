namespace ThreeDScan.Domain.Models;

/// <summary>操作稽核紀錄（對應 OperationLog 表）。</summary>
public sealed class OperationLogEntry
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.Now;

    public required string TraceId { get; init; }

    public required string ClientId { get; init; }

    public string? Operator { get; init; }

    public required string Action { get; init; }

    public string? Target { get; init; }

    /// <summary>Success 或 Fail。</summary>
    public required string Result { get; init; }

    public string? ErrorCode { get; init; }

    public string? Detail { get; init; }
}
