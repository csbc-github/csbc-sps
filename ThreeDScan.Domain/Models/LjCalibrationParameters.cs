namespace ThreeDScan.Domain.Models;

/// <summary>LJ 量測參數（appsettings 預設值，可與 ScannerConfig 合併）。</summary>
public sealed class LjCalibrationParameters
{
    public double ExpectedBallDiameter { get; init; } = 24.9;
    public double DiameterTolerance { get; init; } = 1.0;
    public int BallCount { get; init; } = 3;
    public double MaxDiameterError { get; init; } = 5.0;

    /// <summary>滾輪／基準面平面 ROI（LPlaneTool）。</summary>
    public DatumPlaneSettings DatumPlane { get; init; } = new();
}
