namespace ThreeDScan.Domain.Models;

/// <summary>任務內嵌的校正提交。對應 Hank <c>Scan3D.Service.CalibrationDocument</c>。</summary>
public sealed class CalibrationCommitDocument
{
    /// <summary>校正識別。</summary>
    public string CalibrationId { get; init; } = "";

    /// <summary>掃描器編號。</summary>
    public string ScannerId { get; init; } = "";

    /// <summary>補償（mm）。</summary>
    public Point3 Offset { get; init; } = new Point3();

    /// <summary>XY RMSE（mm）。</summary>
    public double RmseXY { get; init; }

    /// <summary>Z 偏差（mm）。</summary>
    public double ZDeviation { get; init; }

    /// <summary>校正完成時刻。</summary>
    public DateTimeOffset CalibratedAt { get; init; }

    /// <summary>方法名稱。</summary>
    public string Method { get; init; } = "";

    /// <summary>來源任務編號。</summary>
    public string? SourceTaskId { get; init; }
}
