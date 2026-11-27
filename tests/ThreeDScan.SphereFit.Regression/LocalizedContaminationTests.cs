using ThreeDScan.Infrastructure.Measure;

internal static class LocalizedContaminationTests
{
    internal static IEnumerable<CaseResult> Run()
    {
        yield return Check("localized-measured-drop-retains-accurate-center-and-raw-diagnostics", () =>
        {
            var frame = AnalyticalTests.Sphere(24.9, noise: .03, alteration: (x, y, z, _) =>
                x >= 1 && x <= 4.2 && y >= -1.5 && y <= 2.8 ? z - 1.2 : z);
            var original = (ushort[])frame.Heights.Clone();
            var free = RobustSphereFitter.Fit(frame, 125.3, 87.7, 24.9, .5);
            var fit = RobustSphereFitter.FitCalibrationSphere(frame, 125.3, 87.7, 24.9, .5);
            Require(!free.IsGeometryValid && free.CoreP95Mm > .8, "Fixture did not expose the intended untrimmed raw-tail failure.");
            Require(fit.Success && fit.UsedLocalizedOutlierValidation, fit.FailureReason);
            Require(Distance(fit) <= .05, $"Localized drop biased the known center by {Distance(fit)} mm.");
            Require(fit.CenterSensitivityMm > 0 && fit.CenterSensitivityMm <= .2, "Independent sector-deletion center stability was not established.");
            Require(Math.Abs(fit.DiameterMm - free.DiameterMm) < 1e-5
                && Math.Abs(fit.FullFreeCoreP95Mm - free.CoreP95Mm) < 1e-5,
                "Localized validation replaced the original free diameter/raw-tail diagnostic.");
            Require(original.AsSpan().SequenceEqual(frame.Heights), "Measured low samples were changed or filled.");
            return fit;
        });
        yield return Check("broad-core-corruption-remains-unusable", () =>
        {
            var fit = RobustSphereFitter.FitCalibrationSphere(AnalyticalTests.Sphere(24.9, noise: .03,
                alteration: (x, y, z, _) => x * x + y * y <= 4.1 * 4.1 ? z - 1.2 : z), 125.3, 87.7, 24.9, .5);
            Require(!fit.Success, "Corruption over more than a quarter of the measured core was accepted.");
            return fit;
        });
        yield return Check("half-core-correlated-deformation-remains-unusable", () =>
        {
            var fit = RobustSphereFitter.FitCalibrationSphere(AnalyticalTests.Sphere(24.9, noise: .03,
                alteration: (x, y, z, _) => x <= 0 && x * x + y * y <= 7.47 * 7.47 ? z - 1.2 : z), 125.3, 87.7, 24.9, .5);
            Require(!fit.Success, "A correlated half-cap deformation was accepted as a reliable standard sphere.");
            return fit;
        });
        yield return Check("localized-missing-pixels-remain-unmodified", () =>
        {
            var frame = AnalyticalTests.Sphere(24.9, noise: .03, alteration: (x, y, z, _) =>
                x >= 1 && x <= 4.2 && y >= -1.5 && y <= 2.8 ? double.NaN : z);
            var original = (ushort[])frame.Heights.Clone();
            var fit = RobustSphereFitter.FitCalibrationSphere(frame, 125.3, 87.7, 24.9, .5);
            Require(fit.Success, fit.FailureReason);
            Require(Distance(fit) <= .05, "A small measured hole biased the recovered center.");
            Require(original.AsSpan().SequenceEqual(frame.Heights), "Invalid source pixels were invented or changed.");
            return fit;
        });
    }

    private static double Distance(RobustSphereFitResult fit) =>
        Math.Sqrt(Math.Pow(fit.X - 125.3, 2) + Math.Pow(fit.Y - 87.7, 2) + Math.Pow(fit.Z - 30, 2));
    private static CaseResult Check(string name, Func<RobustSphereFitResult> run)
    {
        try { return new(name, true, null, run()); }
        catch (Exception error) { return new(name, false, error.Message, null); }
    }
    private static void Require(bool condition, string? error)
    {
        if (!condition) throw new InvalidOperationException(error ?? "Condition failed.");
    }
}
