namespace ThreeDScan.Infrastructure.Repositories.Models;

/// <summary>對應資料表 RollerCalibration。每台光達一列。</summary>
public sealed class RollerCalibrationEntity
{
    /// <summary>光達感測器編號。</summary>
    public string ScannerId { get; set; } = "";

    /// <summary>掃描器到滾輪的距離 (mm)。</summary>
    public double ZDistance { get; set; }

    /// <summary>滾輪直線係數 a。</summary>
    public double LineA { get; set; }

    /// <summary>滾輪直線係數 b。</summary>
    public double LineB { get; set; }

    /// <summary>滾輪直線係數 c。</summary>
    public double LineC { get; set; }

    /// <summary>校正完成時間。</summary>
    public DateTimeOffset CalibratedAt { get; set; }

    /// <summary>追溯 ID。</summary>
    public string TraceId { get; set; } = "";
}
