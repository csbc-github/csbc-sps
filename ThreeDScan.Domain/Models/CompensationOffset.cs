namespace ThreeDScan.Domain.Models;

/// <summary>儀器校正補償值一筆記錄（對應 CompensationHistory 資料表）。</summary>
public sealed class CompensationOffset
{
    public required string ScannerId { get; init; }
    public double OffsetX { get; init; }
    public double OffsetY { get; init; }
    public double OffsetZ { get; init; }
    public double Rmse { get; init; }
    public string MachineArea { get; init; } = "";
    public DateTimeOffset CalibratedAt { get; init; }
    public string? Operator { get; init; }
    public string TraceId { get; init; } = "";
}
