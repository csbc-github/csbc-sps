namespace ThreeDScan.Domain.Models;

/// <summary>UC-01 兩次拍攝任務（Seq=1 暫存，對應 InstrumentCalibrationTask）。</summary>
public sealed class InstrumentCalibrationTaskRecord
{
    public const string StatusPending = "Pending";
    public const string StatusCompleted = "Completed";
    public const string StatusExpired = "Expired";

    public required string TaskId { get; init; }
    public required string ScannerId { get; init; }
    public required string Status { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
    public double ExpectedX { get; init; }
    public double ExpectedY { get; init; }
    public double ExpectedZ { get; init; }
    public int BallCount { get; init; }
    public double ScannerToFloorZ { get; init; }
    public double ActualX { get; init; }
    public double ActualY { get; init; }
    public double Rmse { get; init; }
    public string? FrameJson { get; init; }
    public string? TraceIdSeq1 { get; init; }
}
