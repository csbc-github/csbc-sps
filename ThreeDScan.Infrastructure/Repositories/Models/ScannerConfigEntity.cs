namespace ThreeDScan.Infrastructure.Repositories.Models;

/// <summary>對應資料表 ScannerConfig。</summary>
public sealed class ScannerConfigEntity
{
    /// <summary>掃描器編號。</summary>
    public string ScannerId { get; set; } = "";

    /// <summary>Ref 圖樣 X (mm)。</summary>
    public double RefPatternX { get; set; }

    /// <summary>Ref 圖樣 Y (mm)。</summary>
    public double RefPatternY { get; set; }

    /// <summary>Ref 球心 X (mm)。</summary>
    public double RefSphereX { get; set; }

    /// <summary>Ref 球心 Y (mm)。</summary>
    public double RefSphereY { get; set; }

    /// <summary>Contents 資料夾路徑。</summary>
    public string ContentsFolder { get; set; } = "";

    /// <summary>最後更新時刻。</summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>最後更新者。</summary>
    public string? UpdatedBy { get; set; }

    /// <summary>追溯 ID。</summary>
    public string TraceId { get; set; } = "";

    /// <summary>預期球直徑 (mm)。</summary>
    public double? ExpectedBallDiameter { get; set; }

    /// <summary>球徑容差 (mm)。</summary>
    public double? DiameterTolerance { get; set; }

    /// <summary>球徑允許最大誤差 (mm)。</summary>
    public double? MaxDiameterError { get; set; }

    /// <summary>預期球數。</summary>
    public int? BallCount { get; set; }

    /// <summary>是否啟用基準面量測。</summary>
    public bool? DatumPlaneEnabled { get; set; }

    /// <summary>基準面 ROI 中心 X (mm)。</summary>
    public double? DatumPlaneCenterX { get; set; }

    /// <summary>基準面 ROI 中心 Y (mm)。</summary>
    public double? DatumPlaneCenterY { get; set; }

    /// <summary>基準面 ROI 寬度 (mm)。</summary>
    public double? DatumPlaneWidth { get; set; }

    /// <summary>基準面 ROI 高度 (mm)。</summary>
    public double? DatumPlaneHeight { get; set; }

    /// <summary>基準面 ROI 旋轉角（度）。</summary>
    public double? DatumPlaneAngle { get; set; }
}
