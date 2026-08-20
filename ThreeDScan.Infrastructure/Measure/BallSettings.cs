namespace ThreeDScan.Infrastructure.Measure;

/// <summary>Static bridge so ported ljd_measure can call BallSettings.* without App.config.</summary>
internal static class BallSettings
{
    private static readonly AsyncLocal<LjMeasureSettings?> Current = new();

    public static void SetScope(LjMeasureSettings settings)
    {
        Current.Value = settings;
    }

    public static void ClearScope()
    {
        Current.Value = null;
    }

    private static LjMeasureSettings S
    {
        get
        {
            return Current.Value ?? throw new InvalidOperationException("LjMeasureSettings scope not set.");
        }
    }

    public static double ExpectedBallDiameter
    {
        get
        {
            return S.ExpectedBallDiameter;
        }
    }
    public static double DiameterTolerance
    {
        get
        {
            return S.DiameterTolerance;
        }
    }
    public static int BallCount
    {
        get
        {
            return S.BallCount;
        }
    }
    public static double RefPatternX
    {
        get
        {
            return S.RefPatternX;
        }
    }
    public static double RefPatternY
    {
        get
        {
            return S.RefPatternY;
        }
    }
    public static double RefSphereX
    {
        get
        {
            return S.RefSphereX;
        }
    }
    public static double RefSphereY
    {
        get
        {
            return S.RefSphereY;
        }
    }
    public static double PatternRadius
    {
        get
        {
            return S.PatternRadius;
        }
    }
    public static double SphereRadius
    {
        get
        {
            return S.SphereRadius;
        }
    }
    public static double MaxDiameterError
    {
        get
        {
            return S.MaxDiameterError;
        }
    }
    public static bool DatumPlaneEnabled
    {
        get
        {
            return S.DatumPlaneEnabled;
        }
    }
    public static double DatumPlaneCenterX
    {
        get
        {
            return S.DatumPlaneCenterX;
        }
    }
    public static double DatumPlaneCenterY
    {
        get
        {
            return S.DatumPlaneCenterY;
        }
    }
    public static double DatumPlaneWidth
    {
        get
        {
            return S.DatumPlaneWidth;
        }
    }
    public static double DatumPlaneHeight
    {
        get
        {
            return S.DatumPlaneHeight;
        }
    }
    public static double DatumPlaneAngle
    {
        get
        {
            return S.DatumPlaneAngle;
        }
    }
    public static double MinSphereCenterSeparationMm
    {
        get
        {
            return S.MinSphereCenterSeparationMm;
        }
    }
    public static double CalibrationDiscoverSearchRadiusMm
    {
        get
        {
            return S.CalibrationDiscoverSearchRadiusMm;
        }
    }
    public static double CalibrationDiscoverGridStepMm
    {
        get
        {
            return S.CalibrationDiscoverGridStepMm;
        }
    }
    public static double CalibrationDiscoverMaxOffsetFromSeedMm
    {
        get
        {
            return S.CalibrationDiscoverMaxOffsetFromSeedMm;
        }
    }
    public static double CalibrationDiscoverAbsoluteMaxMm
    {
        get
        {
            return S.CalibrationDiscoverAbsoluteMaxMm;
        }
    }
    public static string ScannerProgramDescription
    {
        get
        {
            return "ThreeDScan";
        }
    }

    public static bool IsExpectedDiameter(double d)
    {
        return S.IsExpectedDiameter(d);
    }
    public static bool IsAcceptableSphereDiameter(double d)
    {
        return S.IsAcceptableSphereDiameter(d);
    }
}
