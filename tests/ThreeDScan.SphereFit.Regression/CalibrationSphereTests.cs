using ThreeDScan.Infrastructure.Export.Models;
using ThreeDScan.Infrastructure.Measure;

internal static class CalibrationSphereTests
{
    internal static IEnumerable<CaseResult> Run()
    {
        yield return Check("calibration-known-radius-center-and-free-diameter", () =>
        {
            var fit = Fit(AnalyticalTests.Sphere(24.5, noise: .08));
            Require(fit.Success, fit.FailureReason);
            Require(fit.UsesKnownRadius && fit.IsDiameterWithinTolerance, "Calibration mode/diameter status missing.");
            Near(fit.NominalDiameterMm, 24.5, 1e-10, "nominal diameter");
            Near(fit.DiameterMm, 24.5, .12, "free diagnostic diameter");
            Near(fit.X, 125.3, .025, "known-radius X"); Near(fit.Y, 87.7, .025, "known-radius Y");
            Near(fit.Z, 30, .025, "known-radius center Z");
            return fit;
        });
        yield return Check("calibration-does-not-overwrite-free-diameter", () =>
        {
            var fit = Fit(AnalyticalTests.Sphere(24.8, noise: .04));
            Require(fit.Success, fit.FailureReason);
            Require(fit.UsesKnownRadius && fit.IsDiameterWithinTolerance, "Calibration flags incorrect.");
            Near(fit.DiameterMm, 24.8, .08, "retained independent diameter");
            Require(Math.Abs(fit.DiameterMm - fit.NominalDiameterMm) > .2, "Free diameter overwritten with nominal.");
            return fit;
        });
        yield return Check("calibration-noisy-standard-sphere-remains-usable", () =>
        {
            var fit = Fit(AnalyticalTests.Sphere(24.5, noise: .25));
            Require(fit.Success, fit.FailureReason);
            Near(fit.Z, 30, .05, "noisy known-radius center Z");
            Require(fit.UsesKnownRadius, "Noisy standard sphere did not use its known radius.");
            return fit;
        });
        foreach (double noise in new[] { .04, .15, .25 })
        {
            double sigma = noise;
            yield return Check($"calibration-official-24p9-standard-noise-{sigma:G}", () =>
            {
                var fit = RobustSphereFitter.FitCalibrationSphere(AnalyticalTests.Sphere(24.9, noise: sigma),
                    125.3, 87.7, 24.9, .5);
                Require(fit.Success && fit.UsesKnownRadius, fit.FailureReason);
                Near(fit.NominalDiameterMm, 24.9, 1e-10, "official nominal diameter");
                Near(fit.Z, 30, .05, "official standard center Z");
                return fit;
            });
            yield return Check($"calibration-official-24p9-rejects-27mm-noise-{sigma:G}", () =>
            {
                var fit = RobustSphereFitter.FitCalibrationSphere(AnalyticalTests.Sphere(27, noise: sigma),
                    125.3, 87.7, 24.9, .5);
                Require(!fit.Success, $"True 27 mm sphere accepted under official 24.9 mm nominal at sigma={sigma}.");
                return fit;
            });
            yield return Check($"calibration-true-27mm-rejected-noise-{sigma:G}", () =>
            {
                var frame = AnalyticalTests.Sphere(27, noise: sigma);
                var free = RobustSphereFitter.Fit(frame, 125.3, 87.7, 24.5, .5);
                Require(free.DiameterMm > 25, "The deliberately oversized cap no longer has an oversized free measurement.");
                var fit = Fit(frame);
                Require(!fit.Success, $"A true 27 mm cap with sigma={sigma} was accepted as known 24.5 mm: {fit}.");
                return fit;
            });
        }
        yield return Check("calibration-flat-plane-rejected", () =>
        {
            var fit = Fit(AnalyticalTests.Sphere(24.5, alteration: (x, y, _, _) => 40 + .02 * x - .01 * y));
            Require(!fit.Success, "Flat plane accepted by calibration center fit."); return fit;
        });
        yield return Check("calibration-cylinder-rejected", () =>
        {
            var fit = Fit(AnalyticalTests.Sphere(24.5,
                alteration: (x, _, _, _) => 30 + Math.Sqrt(12.25 * 12.25 - x * x)));
            Require(!fit.Success, "Cylindrical support accepted by calibration center fit."); return fit;
        });
        yield return Check("calibration-incompatible-independent-ring-rejected", () =>
        {
            var fit = Fit(AnalyticalTests.Sphere(24.5, alteration: (x, y, z, _) =>
                x * x + y * y > 7.45 * 7.45 ? z - 2 : z));
            Require(!fit.Success, "A clean core concealed incompatible independent outer samples."); return fit;
        });
        yield return Check("calibration-known-radius-recenter-z-stability", () =>
        {
            var frame = AnalyticalTests.Sphere(24.5, noise: .08);
            var centered = Fit(frame);
            foreach ((double dx, double dy) in new[] { (2d, -1.5d), (-1.5d, 1d), (0d, 1.8d) })
            {
                var movedSeed = RobustSphereFitter.FitCalibrationSphere(frame, 125.3 + dx, 87.7 + dy, 24.5, .5);
                Require(movedSeed.Success, movedSeed.FailureReason);
                Near(movedSeed.Z, centered.Z, .025, "center Z across offset seeds");
                Near(movedSeed.X, centered.X, .025, "center X across offset seeds");
                Near(movedSeed.Y, centered.Y, .025, "center Y across offset seeds");
            }
            return centered;
        });
        yield return Check("calibration-raw-height-translation-and-immutability", () =>
        {
            var frame = AnalyticalTests.Sphere(24.5, noise: .08);
            var original = (ushort[])frame.Heights.Clone();
            var fit = Fit(frame);
            var shifted = new HeightFrame
            {
                Info = frame.Info, ProfileCount = frame.ProfileCount,
                Heights = frame.Heights.Select(v => v == 0 ? (ushort)0 : checked((ushort)(v + 1000))).ToArray(),
            };
            var shiftedOriginal = (ushort[])shifted.Heights.Clone();
            var translated = Fit(shifted);
            Require(fit.Success && translated.Success, fit.FailureReason ?? translated.FailureReason);
            Near(translated.X, fit.X, 1e-5, "height-invariant X"); Near(translated.Y, fit.Y, 1e-5, "height-invariant Y");
            Near(translated.Z - fit.Z, 10.2, 1e-5, "translated known center Z");
            Near(translated.DiameterMm, fit.DiameterMm, 1e-5, "height-invariant free diameter");
            Require(original.AsSpan().SequenceEqual(frame.Heights) && shiftedOriginal.AsSpan().SequenceEqual(shifted.Heights),
                "Calibration fit modified raw source pixels.");
            return fit;
        });
        yield return Check("calibration-full-frame-crop-preserves-sample-partition", () =>
        {
            var frame = AnalyticalTests.Sphere(24.5, noise: .15);
            const int shiftX = 5, shiftY = 4;
            int width = frame.Info.XPointCount - shiftX, height = frame.ProfileCount - shiftY;
            var raw = new ushort[width * height];
            for (int row = 0; row < height; row++)
                Array.Copy(frame.Heights, (row + shiftY) * frame.Info.XPointCount + shiftX, raw, row * width, width);
            var crop = new HeightFrame
            {
                Info = new HeightInfo
                {
                    XPointCount = (ushort)width, YLineCount = (ushort)height,
                    XStart = frame.Info.XStart + shiftX * (int)frame.Info.PitchX,
                    YStart = frame.Info.YStart + shiftY * (int)frame.Info.PitchY,
                    PitchX = frame.Info.PitchX, PitchY = frame.Info.PitchY, PitchZ = frame.Info.PitchZ,
                },
                ProfileCount = height, Heights = raw,
            };
            var fullFit = Fit(frame); var cropFit = Fit(crop);
            Require(fullFit.Success && cropFit.Success, fullFit.FailureReason ?? cropFit.FailureReason);
            Near(cropFit.X, fullFit.X, 1e-5, "crop-invariant calibration X");
            Near(cropFit.Y, fullFit.Y, 1e-5, "crop-invariant calibration Y");
            Near(cropFit.Z, fullFit.Z, 1e-5, "crop-invariant calibration Z");
            Near(cropFit.DiameterMm, fullFit.DiameterMm, 1e-5, "crop-invariant diagnostic diameter");
            return cropFit;
        });
        yield return Check("calibration-cancellation-observed", () =>
        {
            using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
            try { RobustSphereFitter.FitCalibrationSphere(AnalyticalTests.Sphere(24.5), 125.3, 87.7, 24.5, .5, cancellation.Token); }
            catch (OperationCanceledException) { return null; }
            throw new InvalidOperationException("Calibration fit did not observe cancellation.");
        });
    }

    private static RobustSphereFitResult Fit(HeightFrame frame) => RobustSphereFitter.FitCalibrationSphere(frame, 125.3, 87.7, 24.5, .5);
    private static CaseResult Check(string name, Func<RobustSphereFitResult?> run)
    {
        try { return new(name, true, null, run()); }
        catch (Exception error) { return new(name, false, error.Message, null); }
    }
    private static void Require(bool condition, string? error)
    {
        if (!condition) throw new InvalidOperationException(error ?? "Condition failed.");
    }
    private static void Near(double actual, double expected, double tolerance, string name) =>
        Require(Math.Abs(actual - expected) <= tolerance, $"{name}: expected {expected}±{tolerance}, actual {actual}.");
}
