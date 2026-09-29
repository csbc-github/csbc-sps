namespace ThreeDScan.Domain.Models;

/// <summary>每台掃描器一筆 LJ Ref／量測參數（DB: dbo.ScannerConfig）；與拍攝群組無關。</summary>
public sealed class ScannerConfigRecord
{
    /// <summary>掃描器編號。</summary>
    public required string ScannerId { get; init; }

    /// <summary>Ref 圖樣 X (mm)。</summary>
    public double RefPatternX { get; init; }

    /// <summary>Ref 圖樣 Y (mm)。</summary>
    public double RefPatternY { get; init; }

    /// <summary>Ref 球心 X (mm)。</summary>
    public double RefSphereX { get; init; }

    /// <summary>Ref 球心 Y (mm)。</summary>
    public double RefSphereY { get; init; }

    /// <summary>Contents 資料夾路徑。</summary>
    public required string ContentsFolder { get; init; }

    /// <summary>最後更新時刻。</summary>
    public DateTimeOffset UpdatedAt { get; init; }

    /// <summary>最後更新者。</summary>
    public string? UpdatedBy { get; init; }

    /// <summary>追溯 ID。</summary>
    public string TraceId { get; init; } = "";

    /// <summary>預期球直徑 (mm)；NULL 時用 appsettings。</summary>
    public double? ExpectedBallDiameter { get; init; }

    /// <summary>球徑容差 (mm)。</summary>
    public double? DiameterTolerance { get; init; }

    /// <summary>球徑允許最大誤差 (mm)。</summary>
    public double? MaxDiameterError { get; init; }

    /// <summary>預期球數。</summary>
    public int? BallCount { get; init; }

    /// <summary>是否啟用基準面量測。</summary>
    public bool? DatumPlaneEnabled { get; init; }

    /// <summary>基準面 ROI 中心 X (mm)。</summary>
    public double? DatumPlaneCenterX { get; init; }

    /// <summary>基準面 ROI 中心 Y (mm)。</summary>
    public double? DatumPlaneCenterY { get; init; }

    /// <summary>基準面 ROI 寬度 (mm)。</summary>
    public double? DatumPlaneWidth { get; init; }

    /// <summary>基準面 ROI 高度 (mm)。</summary>
    public double? DatumPlaneHeight { get; init; }

    /// <summary>基準面 ROI 旋轉角（度）。</summary>
    public double? DatumPlaneAngle { get; init; }
}
