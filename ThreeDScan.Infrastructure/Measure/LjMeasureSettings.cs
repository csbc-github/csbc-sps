using ThreeDScan.Domain.Abstractions;
using ThreeDScan.Domain.Models;

namespace ThreeDScan.Infrastructure.Measure;

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

    public double ExpectedBallDiameter
    {
        get
        {
            return _p.ExpectedBallDiameter;
        }
    }
    public double DiameterTolerance
    {
        get
        {
            return _p.DiameterTolerance;
        }
    }
    public int BallCount
    {
        get
        {
            return _p.BallCount;
        }
    }
    public double MaxDiameterError
    {
        get
        {
            return _p.MaxDiameterError;
        }
    }

    public bool DatumPlaneEnabled
    {
        get
        {
            return _p.DatumPlane.Enabled;
        }
    }
    public double DatumPlaneCenterX
    {
        get
        {
            return _p.DatumPlane.CenterX;
        }
    }
    public double DatumPlaneCenterY
    {
        get
        {
            return _p.DatumPlane.CenterY;
        }
    }
    public double DatumPlaneWidth
    {
        get
        {
            return _p.DatumPlane.Width;
        }
    }
    public double DatumPlaneHeight
    {
        get
        {
            return _p.DatumPlane.Height;
        }
    }
    public double DatumPlaneAngle
    {
        get
        {
            return _p.DatumPlane.Angle;
        }
    }

    public double RefPatternX
    {
        get
        {
            return _config.RefPatternX;
        }
    }
    public double RefPatternY
    {
        get
        {
            return _config.RefPatternY;
        }
    }
    public double RefSphereX
    {
        get
        {
            return _config.RefSphereX;
        }
    }
    public double RefSphereY
    {
        get
        {
            return _config.RefSphereY;
        }
    }

    public double BallRadiusMm
    {
        get
        {
            return ExpectedBallDiameter * 0.5;
        }
    }
    public double PatternRadius
    {
        get
        {
            return BallRadiusMm * 1.02;
        }
    }

    public double SphereRadius
    {
        get
        {
            return BallRadiusMm * 0.76;
        }
    }

    public double MinSphereCenterSeparationMm
    {
        get
        {
            return ExpectedBallDiameter;
        }
    }

    /// <summary>
    /// 校正尋球網格／PM 候選的最大搜尋半徑（mm）。
    /// 至少 25 mm，供手放球偏離種子；會讓寫回的 Ref 離開手動點的位置。
    /// </summary>
    public double CalibrationDiscoverMaxOffsetFromSeedMm
    {
        get
        {
            return Math.Max(25.0, ExpectedBallDiameter);
        }
    }

    /// <summary>避免全視野掃描的安全上限 (mm)。</summary>
    public double CalibrationDiscoverAbsoluteMaxMm
    {
        get
        {
            return 45.0;
        }
    }

    public double CalibrationDiscoverSearchRadiusMm
    {
        get
        {
            return Math.Min(CalibrationDiscoverAbsoluteMaxMm, CalibrationDiscoverMaxOffsetFromSeedMm);
        }
    }

    public double CalibrationDiscoverGridStepMm
    {
        get
        {
            return 3.0;
        }
    }

    public bool IsExpectedDiameter(double measured)
    {
        return Math.Abs(measured - ExpectedBallDiameter) <= DiameterTolerance;
    }

    public bool IsAcceptableSphereDiameter(double measured)
    {
        if (double.IsNaN(measured) || double.IsInfinity(measured)) return false;
        return Math.Abs(measured - ExpectedBallDiameter) <= MaxDiameterError;
    }
}
