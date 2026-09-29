namespace ThreeDScan.Infrastructure.Repositories.Models;

/// <summary>對應資料表 InstrumentCalibrationTask。</summary>
public sealed class InstrumentCalibrationTaskEntity
{
    /// <summary>任務識別碼。</summary>
    public string TaskId { get; set; } = "";

    /// <summary>掃描器編號。</summary>
    public string ScannerId { get; set; } = "";

    /// <summary>Pending／Completed／Expired。</summary>
    public string Status { get; set; } = "";

    /// <summary>建立時刻。</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>最後更新時刻。</summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>逾時時刻。</summary>
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>預期掃描器位置 X (mm)。</summary>
    public double ExpectedX { get; set; }

    /// <summary>預期掃描器位置 Y (mm)。</summary>
    public double ExpectedY { get; set; }

    /// <summary>預期掃描器位置 Z (mm)。</summary>
    public double ExpectedZ { get; set; }

    /// <summary>標準球數量。</summary>
    public int BallCount { get; set; }

    /// <summary>掃描器至地面 Z (mm)。</summary>
    public double ScannerToFloorZ { get; set; }

    /// <summary>第一次拍攝推得的實際 X (mm)。</summary>
    public double ActualX { get; set; }

    /// <summary>第一次拍攝推得的實際 Y (mm)。</summary>
    public double ActualY { get; set; }

    /// <summary>第一次拍攝 RMSE (mm)。</summary>
    public double Rmse { get; set; }

    /// <summary>第一次拍攝幀資料 JSON。</summary>
    public string? FrameJson { get; set; }

    /// <summary>第一次拍攝追溯 ID。</summary>
    public string? TraceIdSeq1 { get; set; }
}
