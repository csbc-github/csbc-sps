namespace ThreeDScan.Domain.Models;

/// <summary>儀器校正補償值一筆記錄（對應 CompensationHistory 資料表）。</summary>
public sealed class CompensationOffset
{
    /// <summary>掃描器編號。</summary>
    public required string ScannerId { get; init; }

    /// <summary>補償 X (mm)。</summary>
    public double OffsetX { get; init; }

    /// <summary>補償 Y (mm)。</summary>
    public double OffsetY { get; init; }

    /// <summary>補償 Z (mm)。</summary>
    public double OffsetZ { get; init; }

    /// <summary>校正 RMSE (mm)。</summary>
    public double Rmse { get; init; }

    /// <summary>產線區域（printing／cutting）。</summary>
    public string MachineArea { get; init; } = "";

    /// <summary>校正完成時刻。</summary>
    public DateTimeOffset CalibratedAt { get; init; }

    /// <summary>操作者。</summary>
    public string? Operator { get; init; }

    /// <summary>追溯 ID。</summary>
    public string TraceId { get; init; } = "";
}
