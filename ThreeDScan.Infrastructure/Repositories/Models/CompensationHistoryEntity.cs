namespace ThreeDScan.Infrastructure.Repositories.Models;

/// <summary>對應資料表 CompensationHistory。</summary>
public sealed class CompensationHistoryEntity
{
    /// <summary>主鍵。</summary>
    public long Id { get; set; }

    /// <summary>掃描器編號。</summary>
    public string ScannerId { get; set; } = "";

    /// <summary>補償 X (mm)。</summary>
    public double OffsetX { get; set; }

    /// <summary>補償 Y (mm)。</summary>
    public double OffsetY { get; set; }

    /// <summary>補償 Z (mm)。</summary>
    public double OffsetZ { get; set; }

    /// <summary>校正 RMSE (mm)。</summary>
    public double Rmse { get; set; }

    /// <summary>產線區域（printing／cutting）。</summary>
    public string MachineArea { get; set; } = "";

    /// <summary>校正完成時刻。</summary>
    public DateTimeOffset CalibratedAt { get; set; }

    /// <summary>操作者。</summary>
    public string? Operator { get; set; }

    /// <summary>追溯 ID。</summary>
    public string TraceId { get; set; } = "";
}
