namespace ThreeDScan.Domain.Models;

/// <summary>UC-01 兩次拍攝任務（Seq=1 暫存，對應 InstrumentCalibrationTask）。</summary>
public sealed class InstrumentCalibrationTaskRecord
{
    /// <summary>等待第二次拍攝。</summary>
    public const string StatusPending = "Pending";

    /// <summary>兩次拍攝已完成。</summary>
    public const string StatusCompleted = "Completed";

    /// <summary>逾時未完成。</summary>
    public const string StatusExpired = "Expired";

    /// <summary>任務識別碼。</summary>
    public required string TaskId { get; init; }

    /// <summary>掃描器編號。</summary>
    public required string ScannerId { get; init; }

    /// <summary>Pending／Completed／Expired。</summary>
    public required string Status { get; init; }

    /// <summary>建立時刻。</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>最後更新時刻。</summary>
    public DateTimeOffset UpdatedAt { get; init; }

    /// <summary>逾時時刻。</summary>
    public DateTimeOffset ExpiresAt { get; init; }

    /// <summary>預期掃描器位置 X (mm)。</summary>
    public double ExpectedX { get; init; }

    /// <summary>預期掃描器位置 Y (mm)。</summary>
    public double ExpectedY { get; init; }

    /// <summary>預期掃描器位置 Z (mm)。</summary>
    public double ExpectedZ { get; init; }

    /// <summary>標準球數量。</summary>
    public int BallCount { get; init; }

    /// <summary>掃描器至地面 Z (mm)。</summary>
    public double ScannerToFloorZ { get; init; }

    /// <summary>第一次拍攝推得的實際 X (mm)。</summary>
    public double ActualX { get; init; }

    /// <summary>第一次拍攝推得的實際 Y (mm)。</summary>
    public double ActualY { get; init; }

    /// <summary>第一次拍攝 RMSE (mm)。</summary>
    public double Rmse { get; init; }

    /// <summary>第一次拍攝幀資料 JSON。</summary>
    public string? FrameJson { get; init; }

    /// <summary>第一次拍攝追溯 ID。</summary>
    public string? TraceIdSeq1 { get; init; }
}
