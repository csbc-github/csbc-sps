using ThreeDScan.Infrastructure.Export.Models;
using ThreeDScan.Infrastructure.Measure;

internal static class AnalyticalTests
{
    internal static IEnumerable<CaseResult> Run()
    {
        yield return Check("quantized-noisy-known-sphere", () =>
        {
            var frame = Sphere(24.5, noise: .04);
            var fit = Fit(frame);
            Require(fit.Success, fit.FailureReason);
            Near(fit.DiameterMm, 24.5, .06, "diameter");
            Near(fit.X, 125.3, .02, "X"); Near(fit.Y, 87.7, .02, "Y"); Near(fit.Z, 30, .04, "Z");
            return fit;
        });
        yield return Check("free-radius-does-not-force-nominal", () =>
        {
            var fit = Fit(Sphere(24.8, noise: .03));
            Require(fit.Success, fit.FailureReason);
            Near(fit.DiameterMm, 24.8, .06, "independent true diameter");
            Require(Math.Abs(fit.DiameterMm - 24.5) > .2, "Radius was pulled to nominal.");
            return fit;
        });
        yield return Check("out-of-gate-true-sphere-stays-rejected", () =>
        {
            var frame = Sphere(27, noise: .02);
            var rejected = Fit(frame);
            Require(!rejected.Success, "A measured 27 mm sphere passed the 24.5±0.5 gate.");
            Near(rejected.DiameterMm, 27, .06, "rejected measured diameter");
            var accepted = RobustSphereFitter.Fit(frame, 125.3, 87.7, 27, .5);
            Require(accepted.Success, accepted.FailureReason);
            Near(accepted.DiameterMm, 27, .06, "matching nominal measured diameter");
            return rejected;
        });
        yield return Check("measured-rim-contamination-stays-outside-fit", () =>
        {
            var frame = Sphere(24.5, noise: .03, alteration: (x, y, z, i) =>
                x * x + y * y > 7.5 * 7.5 && i % 10 == 0 ? z - 1.2 : z);
            var fit = Fit(frame);
            Require(fit.Success, fit.FailureReason);
            Near(fit.DiameterMm, 24.5, .06, "diameter with contaminated rim");
            Require(fit.ValidationRmsMm > fit.CoreRmsMm * 2, "Holdout did not expose outer contamination.");
            return fit;
        });
        yield return Check("incompatible-independent-ring-rejected", () =>
        {
            var fit = Fit(Sphere(24.5, alteration: (x, y, z, _) =>
                x * x + y * y > 7.45 * 7.45 ? z - 2 : z));
            Require(!fit.Success && fit.CoreRmsMm < .02 && fit.ValidationRmsMm > .5,
                $"A clean core concealed a bad independent ring: {fit}.");
            return fit;
        });
        yield return Check("offset-seed-recenters-measured-center", () =>
        {
            var frame = Sphere(24.5, noise: .03);
            var fit = RobustSphereFitter.Fit(frame, 127.3, 86.2, 24.5, .5);
            Require(fit.Success, fit.FailureReason);
            Near(fit.X, 125.3, .02, "recentered X"); Near(fit.Y, 87.7, .02, "recentered Y");
            Near(fit.DiameterMm, 24.5, .07, "recentered diameter");
            return fit;
        });
        yield return Check("world-origin-and-first-profile-preserved", () =>
        {
            var a = Fit(Sphere(24.5, noise: .03));
            var b = RobustSphereFitter.Fit(Sphere(24.5, 425.3, -112.3, 45, noise: .03, firstProfileNo: 123),
                425.3, -112.3, 24.5, .5);
            Require(a.Success && b.Success, a.FailureReason ?? b.FailureReason);
            Near(b.X - a.X, 300, .002, "translated X"); Near(b.Y - a.Y, -200, .002, "translated Y");
            Near(b.Z - a.Z, 15, .02, "translated Z"); Near(b.DiameterMm, a.DiameterMm, .02, "translation-invariant diameter");
            return b;
        });
        yield return Check("flat-plane-rejected", () =>
        {
            var fit = Fit(Sphere(24.5, alteration: (x, y, _, _) => 40 + .02 * x - .01 * y));
            Require(!fit.Success, "Flat plane accepted as ball."); return fit;
        });
        yield return Check("cylinder-rejected", () =>
        {
            var fit = Fit(Sphere(24.5, alteration: (x, _, _, _) => 30 + Math.Sqrt(12.25 * 12.25 - x * x)));
            Require(!fit.Success, "Cylindrical support accepted as ball."); return fit;
        });
        yield return Check("missing-half-cap-rejected", () =>
        {
            var fit = Fit(Sphere(24.5, alteration: (x, _, z, _) => x > 0 ? double.NaN : z));
            Require(!fit.Success, "An unobserved half cap accepted as complete measurement."); return fit;
        });
        yield return Check("source-height-buffer-immutable", () =>
        {
            var frame = Sphere(24.5, noise: .03);
            var before = (ushort[])frame.Heights.Clone(); var fit = Fit(frame);
            Require(before.AsSpan().SequenceEqual(frame.Heights), "Fitter modified raw height buffer."); return fit;
        });
        yield return Check("cancellation-observed", () =>
        {
            using var cancel = new CancellationTokenSource(); cancel.Cancel();
            try { RobustSphereFitter.Fit(Sphere(24.5), 125.3, 87.7, 24.5, .5, cancel.Token); }
            catch (OperationCanceledException) { return null; }
            throw new InvalidOperationException("Canceled fit did not throw cancellation.");
        });
    }

    private static RobustSphereFitResult Fit(HeightFrame frame) => RobustSphereFitter.Fit(frame, 125.3, 87.7, 24.5, .5);
    private static CaseResult Check(string name, Func<RobustSphereFitResult?> action)
    {
        try { return new(name, true, null, action()); }
        catch (Exception error) { return new(name, false, error.Message, null); }
    }
    private static void Near(double actual, double expected, double tolerance, string quantity) =>
        Require(Math.Abs(actual - expected) <= tolerance, $"{quantity}: expected {expected}±{tolerance}, actual {actual}.");
    private static void Require(bool condition, string? message)
    {
        if (!condition) throw new InvalidOperationException(message ?? "Condition failed.");
    }

    // An independent analytic cap, sampled and quantized as a scanner height image.
    internal static HeightFrame Sphere(double diameter, double cx = 125.3, double cy = 87.7, double cz = 30,
        double noise = 0, Func<double, double, double, int, double>? alteration = null, uint firstProfileNo = 0)
    {
        const int size = 151; const double pitch = .2, zPitch = .0102;
        var raw = new ushort[size * size];
        var random = new Random(293087);
        double radius = diameter / 2;
        for (int row = 0; row < size; row++)
        for (int col = 0; col < size; col++)
        {
            double x = (col - 75) * pitch, y = (row - 75) * pitch, r2 = x * x + y * y;
            if (r2 >= radius * radius) continue;
            double z = cz + Math.Sqrt(radius * radius - r2);
            if (noise != 0) z += noise * Math.Sqrt(-2 * Math.Log(1 - random.NextDouble())) * Math.Cos(2 * Math.PI * random.NextDouble());
            z = alteration?.Invoke(x, y, z, row * size + col) ?? z;
            if (double.IsFinite(z)) raw[row * size + col] = checked((ushort)Math.Round(z / zPitch + 32768));
        }
        return new HeightFrame
        {
            Info = new HeightInfo
            {
                XPointCount = size, YLineCount = size, PitchX = 20000, PitchY = 20000, PitchZ = 1020,
                XStart = checked((int)Math.Round((cx - 15) * 100000)),
                YStart = checked((int)Math.Round((cy - 15 - firstProfileNo * pitch) * 100000)),
            },
            FirstProfileNo = firstProfileNo, ProfileCount = size, Heights = raw,
        };
    }
}

internal sealed record CaseResult(string Name, bool Pass, string? Error, RobustSphereFitResult? Fit);
