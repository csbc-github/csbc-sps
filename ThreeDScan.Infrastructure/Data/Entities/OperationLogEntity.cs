namespace ThreeDScan.Infrastructure.Data.Entities;

public sealed class OperationLogEntity
{
    public long Id { get; set; }

    public DateTimeOffset Timestamp { get; set; }

    public string TraceId { get; set; } = "";

    public string ClientId { get; set; } = "";

    public string? Operator { get; set; }

    public string Action { get; set; } = "";

    public string? Target { get; set; }

    public string Result { get; set; } = "";

    public string? ErrorCode { get; set; }

    public string? Detail { get; set; }
}
