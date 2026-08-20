namespace ThreeDScan.Infrastructure.Data.Entities;

public sealed class InstrumentCalibrationTaskEntity
{
    public string TaskId { get; set; } = "";
    public string ScannerId { get; set; } = "";
    public string Status { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public double ExpectedX { get; set; }
    public double ExpectedY { get; set; }
    public double ExpectedZ { get; set; }
    public int BallCount { get; set; }
    public double ScannerToFloorZ { get; set; }
    public double ActualX { get; set; }
    public double ActualY { get; set; }
    public double Rmse { get; set; }
    public string? FrameJson { get; set; }
    public string? TraceIdSeq1 { get; set; }
}
