using ThreeDScan.Domain.Abstractions;
using ThreeDScan.Domain.Models;

namespace ThreeDScan.Infrastructure.LjSdk;

/// <summary>Replaces LJScanWinform BallSettings — appsettings + ScannerConfig.</summary>
internal sealed class LjMeasureSettings
{
    private readonly LjCalibrationParameters _p;
    private readonly ScannerConfigRecord _config;

    public LjMeasureSettings(LjCalibrationParameters parameters, ScannerConfigRecord config)
    {
        _p = parameters;
        _config = config;
    }

    public double ExpectedBallDiameter => _p.ExpectedBallDiameter;
    public double DiameterTolerance => _p.DiameterTolerance;
    public int BallCount => _p.BallCount;
    public double MaxDiameterError => _p.MaxDiameterError;

    public bool DatumPlaneEnabled => _p.DatumPlane.Enabled;
    public double DatumPlaneCenterX => _p.DatumPlane.CenterX;
    public double DatumPlaneCenterY => _p.DatumPlane.CenterY;
    public double DatumPlaneWidth => _p.DatumPlane.Width;
    public double DatumPlaneHeight => _p.DatumPlane.Height;
    public double DatumPlaneAngle => _p.DatumPlane.Angle;

    public double RefPatternX => _config.RefPatternX;
    public double RefPatternY => _config.RefPatternY;
    public double RefSphereX => _config.RefSphereX;
    public double RefSphereY => _config.RefSphereY;

    public double BallRadiusMm => ExpectedBallDiameter * 0.5;
    public double PatternRadius => BallRadiusMm * 1.02;

    public double SphereRadius => BallRadiusMm * 0.76;

    public double MinSphereCenterSeparationMm => ExpectedBallDiameter;

    /// <summary>校正尋球網格／PM 候選的最大搜尋半徑（依球徑，適用手放球）。</summary>
    public double CalibrationDiscoverMaxOffsetFromSeedMm =>
        Math.Max(25.0, ExpectedBallDiameter);

    /// <summary>避免全視野掃描的安全上限 (mm)。</summary>
    public double CalibrationDiscoverAbsoluteMaxMm => 45.0;

    public double CalibrationDiscoverSearchRadiusMm =>
        Math.Min(CalibrationDiscoverAbsoluteMaxMm, CalibrationDiscoverMaxOffsetFromSeedMm);

    public double CalibrationDiscoverGridStepMm => 3.0;

    public bool IsExpectedDiameter(double measured) =>
        Math.Abs(measured - ExpectedBallDiameter) <= DiameterTolerance;

    public bool IsAcceptableSphereDiameter(double measured)
    {
        if (double.IsNaN(measured) || double.IsInfinity(measured)) return false;
        return Math.Abs(measured - ExpectedBallDiameter) <= MaxDiameterError;
    }
}
