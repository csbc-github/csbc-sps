namespace ThreeDScan.Infrastructure.LjSdk;

/// <summary>Static bridge so ported ljd_measure can call BallSettings.* without App.config.</summary>
internal static class BallSettings
{
    private static readonly AsyncLocal<LjMeasureSettings?> Current = new();

    public static void SetScope(LjMeasureSettings settings) => Current.Value = settings;

    public static void ClearScope() => Current.Value = null;

    private static LjMeasureSettings S =>
        Current.Value ?? throw new InvalidOperationException("LjMeasureSettings scope not set.");

    public static double ExpectedBallDiameter => S.ExpectedBallDiameter;
    public static double DiameterTolerance => S.DiameterTolerance;
    public static int BallCount => S.BallCount;
    public static double RefPatternX => S.RefPatternX;
    public static double RefPatternY => S.RefPatternY;
    public static double RefSphereX => S.RefSphereX;
    public static double RefSphereY => S.RefSphereY;
    public static double PatternRadius => S.PatternRadius;
    public static double SphereRadius => S.SphereRadius;
    public static double MaxDiameterError => S.MaxDiameterError;
    public static bool DatumPlaneEnabled => S.DatumPlaneEnabled;
    public static double DatumPlaneCenterX => S.DatumPlaneCenterX;
    public static double DatumPlaneCenterY => S.DatumPlaneCenterY;
    public static double DatumPlaneWidth => S.DatumPlaneWidth;
    public static double DatumPlaneHeight => S.DatumPlaneHeight;
    public static double DatumPlaneAngle => S.DatumPlaneAngle;
    public static double MinSphereCenterSeparationMm => S.MinSphereCenterSeparationMm;
    public static double CalibrationDiscoverSearchRadiusMm => S.CalibrationDiscoverSearchRadiusMm;
    public static double CalibrationDiscoverGridStepMm => S.CalibrationDiscoverGridStepMm;
    public static double CalibrationDiscoverMaxOffsetFromSeedMm => S.CalibrationDiscoverMaxOffsetFromSeedMm;
    public static double CalibrationDiscoverAbsoluteMaxMm => S.CalibrationDiscoverAbsoluteMaxMm;
    public static string ScannerProgramDescription => "ThreeDScan";

    public static bool IsExpectedDiameter(double d) => S.IsExpectedDiameter(d);
    public static bool IsAcceptableSphereDiameter(double d) => S.IsAcceptableSphereDiameter(d);
}
