namespace ThreeDScan.Domain.Models;

/// <summary>LJ 量測參數（appsettings 預設值，可與 ScannerConfig 合併）。</summary>
public sealed class LjCalibrationParameters
{
    /// <summary>預期標準球直徑 (mm)。</summary>
    public double ExpectedBallDiameter { get; init; } = 24.9;

    /// <summary>球徑容差 (mm)。</summary>
    public double DiameterTolerance { get; init; } = 1.0;

    /// <summary>預期球數。</summary>
    public int BallCount { get; init; } = 3;

    /// <summary>球徑允許最大誤差 (mm)。</summary>
    public double MaxDiameterError { get; init; } = 5.0;

    /// <summary>滾輪／基準面平面 ROI（LPlaneTool）。</summary>
    public DatumPlaneSettings DatumPlane { get; init; } = new();
}
