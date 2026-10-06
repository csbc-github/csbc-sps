namespace ThreeDScan.Domain.Models;

/// <summary>已成功的滾輪校正：掃描器到滾輪平面，供線掃描把截面轉成高度。</summary>
public sealed class RollerBaselineRecord
{
    /// <summary>光達感測器編號。</summary>
    public string ScannerId { get; set; } = "";

    /// <summary>掃描器到滾輪平面的距離 (mm)。</summary>
    public double ZDistance { get; set; }

    /// <summary>滾輪直線 ax+by+c=0 的 a。座標在光達截面。</summary>
    public double LineA { get; set; }

    /// <summary>滾輪直線的 b。</summary>
    public double LineB { get; set; }

    /// <summary>滾輪直線的 c。c 大於 0 時即掃描器到滾輪的距離。</summary>
    public double LineC { get; set; }

    /// <summary>校正完成時間。</summary>
    public DateTimeOffset CalibratedAt { get; set; }

    /// <summary>該次校正的追溯 ID。</summary>
    public string TraceId { get; set; } = "";
}
