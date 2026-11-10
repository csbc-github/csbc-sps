namespace ThreeDScan.Domain.Models;

/// <summary>已成功的滾輪校正：掃描器到滾輪平面，供線掃描把截面轉成高度。</summary>
public sealed class RollerBaselineRecord
{
    /// <summary>光達感測器編號。</summary>
    public string ScannerId { get; set; } = "";

    /// <summary>掃描器到滾輪平面的距離 (mm)。v2 僅契約 zDistance。</summary>
    public double ZDistance { get; set; }

    /// <summary>校正完成時間。</summary>
    public DateTimeOffset CalibratedAt { get; set; }

    /// <summary>該次校正的追溯 ID。</summary>
    public string TraceId { get; set; } = "";
}
