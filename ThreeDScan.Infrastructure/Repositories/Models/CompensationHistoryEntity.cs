namespace ThreeDScan.Infrastructure.Data.Entities;

public sealed class CompensationHistoryEntity
{
    public long Id { get; set; }
    public string ScannerId { get; set; } = "";
    public double OffsetX { get; set; }
    public double OffsetY { get; set; }
    public double OffsetZ { get; set; }
    public double Rmse { get; set; }
    public string MachineArea { get; set; } = "";
    public DateTimeOffset CalibratedAt { get; set; }
    public string? Operator { get; set; }
    public string TraceId { get; set; } = "";
}
