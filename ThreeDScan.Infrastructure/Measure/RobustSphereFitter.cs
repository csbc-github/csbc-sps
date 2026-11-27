using ThreeDScan.Infrastructure.Export.Models;

namespace ThreeDScan.Infrastructure.Measure;

/// <summary>Fits a measured upper spherical cap without fixing its radius to the nominal diameter.</summary>
internal static class RobustSphereFitter
{
    // The steep, less reliable rim is a holdout, never part of the center/radius objective.
    private const double CoreRadiusFraction = .60;
    private const double ValidationRadiusFraction = .76;

    internal static RobustSphereFitResult Fit(HeightFrame frame, double seedX, double seedY,
        double expectedDiameter, double maxDiameterError, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!double.IsFinite(seedX) || !double.IsFinite(seedY) || !double.IsFinite(expectedDiameter)
            || expectedDiameter <= 0 || !double.IsFinite(maxDiameterError) || maxDiameterError < 0)
            return Failed("球面參數必須是有限值，且球徑須大於零。");
        int width = frame.Info.XPointCount, height = frame.ProfileCount;
        if (width == 0 || height <= 0 || frame.Heights.Length != (long)width * height
            || frame.Info.PitchX == 0 || frame.Info.PitchY == 0 || frame.Info.PitchZ == 0)
            return Failed("高度影像尺寸或間距無效。");

        double px = frame.Info.PitchX / 100000d, py = frame.Info.PitchY / 100000d;
        double pz = frame.Info.PitchZ / 100000d;
        double ox = frame.Info.XStart / 100000d;
        double oy = frame.Info.YStart / 100000d + frame.FirstProfileNo * py;
        double coreRadius = expectedDiameter * .5 * CoreRadiusFraction;
        double validationRadius = expectedDiameter * .5 * ValidationRadiusFraction;
        if (coreRadius < 3 * Math.Max(px, py)) return Failed("球面取樣密度不足。");

        double cx = seedX, cy = seedY;
        double recenterShift = double.PositiveInfinity;
        Estimate? model = null;
        Point[] core = [];
        // Fixed recentering procedure, independent of diameter agreement or candidate score.
        for (int pass = 0; pass < 3; pass++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            core = Sample(cx, cy, coreRadius);
            if (core.Length < 100 || core.Length * px * py < Math.PI * coreRadius * coreRadius * .75)
                return Failed("中央球面有效量測不足或遭遮擋。");
            model = EstimateCap(core, cancellationToken);
            if (model is null) return Failed("球面取樣退化或穩健擬合未收斂。");
            if (Math.Sqrt(Math.Pow(model.X - seedX, 2) + Math.Pow(model.Y - seedY, 2)) > 5)
                return Failed("擬合球心偏離圖形搜尋位置超過 5 mm。");
            if (model.Z >= core.Average(p => p.Z)) return Failed("量測不符合球上帽方向，可能為支架或平面。");
            recenterShift = Math.Sqrt(Math.Pow(model.X - cx, 2) + Math.Pow(model.Y - cy, 2));
            cx = model.X;
            cy = model.Y;
        }
        if (recenterShift > Math.Max(.1, Math.Max(px, py) * .5))
            return Failed("重新定位後的球心未穩定，請檢查球面量測品質。");

        var validation = Sample(cx, cy, validationRadius);
        double coverage = validation.Length * px * py / (Math.PI * validationRadius * validationRadius);
        if (coverage < .75 || validation.Length < 100)
            return Failed("球面外圈有效覆蓋不足，可能遭遮擋或超出視野。");
        var fitPixels = core.Select(p => (p.X, p.Y)).ToHashSet();
        var ring = validation.Where(p => Math.Pow(p.X - cx, 2) + Math.Pow(p.Y - cy, 2) > coreRadius * coreRadius
            && !fitPixels.Contains((p.X, p.Y))).ToArray();
        double expectedRingCount = Math.PI * (validationRadius * validationRadius - coreRadius * coreRadius) / (px * py);
        if (ring.Length < expectedRingCount * .65 || ring.Length < 64)
            return Failed("獨立球面外環有效取樣不足。");
        double[] coreErrors = core.Select(p => Math.Abs(Residual(p, model!))).ToArray();
        double[] validationErrors = ring.Select(p => Math.Abs(Residual(p, model!))).ToArray();
        double coreRms = Math.Sqrt(coreErrors.Average(e => e * e));
        double validationRms = Math.Sqrt(validationErrors.Average(e => e * e));
        double inlierFraction = coreErrors.Count(e => e <= .3) / (double)coreErrors.Length;
        double apexDifference = Math.Abs(model!.Z + model.Radius - Quantile(core.Select(p => p.Z).ToArray(), .99));
        var result = new RobustSphereFitResult
        {
            X = model!.X, Y = model.Y, Z = model.Z, DiameterMm = model.Radius * 2,
            CoreRmsMm = coreRms, ValidationRmsMm = validationRms,
            CorePointCount = core.Length, ValidationPointCount = ring.Length,
            InlierFraction = inlierFraction, CoreP95Mm = Quantile(coreErrors, .95),
            ValidationP95Mm = Quantile(validationErrors, .95), CoverageFraction = Math.Min(1, coverage),
            RecenterShiftMm = recenterShift,
            NominalDiameterMm = expectedDiameter,
            IsDiameterWithinTolerance = Math.Abs(model.Radius * 2 - expectedDiameter) <= maxDiameterError,
            FreeFitX = model.X, FreeFitY = model.Y, FreeFitZ = model.Z,
            // All preceding source/coverage/convergence/orientation/drift checks
            // passed. Apex is checked independently of the residual-quality gates.
            HasValidSourceSupport = apexDifference <= .8,
            FullFreeCoreRmsMm = coreRms, FullFreeCoreP95Mm = Quantile(coreErrors, .95),
            FullFreeValidationRmsMm = validationRms, FullFreeValidationP95Mm = Quantile(validationErrors, .95),
        };

        // Quality uses ALL observed samples, not just the points downweighted by IRLS.
        if (coreRms > .35 || result.CoreP95Mm > .8 || inlierFraction < .8)
            return result with { FailureReason = "中央球面殘差或實測支持比例未通過，可能為支架或異常表面。" };
        if (validationRms > .5 || result.ValidationP95Mm > 1
            || !AngularSupport(ring, model, coreRadius, validationRadius, Math.Max(8, expectedRingCount / 16)))
            return result with { FailureReason = "獨立外圈球面檢查未通過；不以少數中央點認定為標準球。" };
        if (apexDifference > .8)
            return result with { FailureReason = "球頂與量測高度不一致。" };
        result = result with { IsGeometryValid = true };
        if (Math.Abs(result.DiameterMm - expectedDiameter) > maxDiameterError)
            return result with { FailureReason = $"實測球徑 {result.DiameterMm:F3} mm 超過 {expectedDiameter:G}±{maxDiameterError:G} mm。" };
        return result with { Success = true };

        Point[] Sample(double x, double y, double radius)
        {
            // A truncated aperture must not masquerade as a well-observed cap.
            if (x - radius < ox - px * .5 || y - radius < oy - py * .5
                || x + radius > ox + (width - .5) * px || y + radius > oy + (height - .5) * py) return [];
            int x0 = Math.Max(0, (int)Math.Ceiling((x - radius - ox) / px));
            int x1 = Math.Min(width - 1, (int)Math.Floor((x + radius - ox) / px));
            int y0 = Math.Max(0, (int)Math.Ceiling((y - radius - oy) / py));
            int y1 = Math.Min(height - 1, (int)Math.Floor((y + radius - oy) / py));
            var points = new List<Point>();
            for (int row = y0; row <= y1; row++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                for (int col = x0; col <= x1; col++)
                {
                    double sx = ox + col * px, sy = oy + row * py;
                    if ((sx - x) * (sx - x) + (sy - y) * (sy - y) > radius * radius) continue;
                    ushort raw = frame.Heights[row * width + col];
                    if (raw != 0) points.Add(new Point(sx, sy, (raw - 32768) * pz));
                }
            }
            return points.ToArray();
        }
    }

    /// <summary>
    /// Fits the center of an independently confirmed standard sphere. The original
    /// all-pixel free fit is retained only as a measurement diagnostic. Two new
    /// models use checkerboard A pixels; all compatibility checks use untouched B
    /// pixels. This is a standard-target center estimator, not a general size test.
    /// </summary>
    internal static RobustSphereFitResult FitCalibrationSphere(HeightFrame frame, double seedX, double seedY,
        double expectedDiameter, double maxDiameterError, CancellationToken cancellationToken = default)
    {
        // The all-pixel fit is a diagnostic and conservative source-sanity veto.
        // It does not initialize either calibration model, choose thresholds, or
        // enter the strictly A/B compatibility comparison. Its size gate may fail.
        var diagnostic = Fit(frame, seedX, seedY, expectedDiameter, maxDiameterError, cancellationToken);
        // Source integrity remains mandatory. Untrimmed free-fit quality is kept
        // as a diagnostic; a localized tail may only be rescued by the bounded
        // independent checks and the training-only sensitivity test below.
        if (!diagnostic.HasValidSourceSupport) return diagnostic;
        if (!double.IsFinite(seedX) || !double.IsFinite(seedY) || !double.IsFinite(expectedDiameter)
            || expectedDiameter <= 0 || !double.IsFinite(maxDiameterError) || maxDiameterError < 0)
            return diagnostic;
        int width = frame.Info.XPointCount, height = frame.ProfileCount;
        if (width == 0 || height <= 0 || frame.Heights.Length != (long)width * height
            || frame.Info.PitchX == 0 || frame.Info.PitchY == 0 || frame.Info.PitchZ == 0)
            return diagnostic;
        var result = diagnostic with { Success = false, FailureReason = null };
        double px = frame.Info.PitchX / 100000d, py = frame.Info.PitchY / 100000d;
        double pz = frame.Info.PitchZ / 100000d;
        double ox = frame.Info.XStart / 100000d;
        double oy = frame.Info.YStart / 100000d + frame.FirstProfileNo * py;
        double radius = expectedDiameter * .5;
        double coreRadius = radius * CoreRadiusFraction;
        double validationRadius = radius * ValidationRadiusFraction;
        if (coreRadius < 3 * Math.Max(px, py)) return Reject("球面取樣解析度不足。");

        // Integer scanner-grid phase survives lossless cropping, including
        // negative origins and a nonzero FirstProfileNo. Local row/column parity
        // alone would swap A/B when a fixture starts on an odd source pixel.
        long gridX = (long)Math.Floor(frame.Info.XStart / (double)frame.Info.PitchX);
        long gridY = (long)Math.Floor(frame.Info.YStart / (double)frame.Info.PitchY) + frame.FirstProfileNo;
        double cx = seedX, cy = seedY, freeShift = double.PositiveInfinity;
        Estimate? comparison = null;
        Aperture training = new([], 0, new int[8]);
        for (int pass = 0; pass < 3; pass++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            training = Sample(cx, cy, 0, coreRadius, true);
            if (!Covered(training, .75, 100)) return Reject("訓練半圖的球面核心觀測不足。");
            comparison = EstimateCap(training.Points, cancellationToken);
            if (comparison is null) return Reject("訓練半圖的自由球面模型未收斂。");
            if (DistanceXY(comparison.X, comparison.Y, seedX, seedY) > 5)
                return Reject("訓練半圖的球面中心偏離偵測位置超過 5 mm。");
            if (comparison.Z >= training.Points.Average(p => p.Z))
                return Reject("訓練半圖不符合可見上半球。");
            freeShift = DistanceXY(comparison.X, comparison.Y, cx, cy);
            cx = comparison.X; cy = comparison.Y;
        }
        if (freeShift > Math.Max(.1, Math.Max(px, py) * .5))
            return Reject("訓練半圖的自由球面中心未穩定。");
        var free = comparison!;
        result = result with
        {
            ComparisonFreeDiameterMm = free.Radius * 2,
            ComparisonFreeX = free.X, ComparisonFreeY = free.Y, ComparisonFreeZ = free.Z,
        };
        var freeTrainingErrors = training.Points.Select(p => Math.Abs(Residual(p, free))).ToArray();
        bool requiresLocalizedValidation = !diagnostic.IsGeometryValid || !CoreQuality(freeTrainingErrors);
        if (!BoundedCoreQuality(freeTrainingErrors))
            return Reject("訓練半圖的自由球面核心支持或污染比例未通過。");

        // Preserve the training model's observed apex when changing radius.
        var known = new Estimate(free.X, free.Y, free.Z + free.Radius - radius, radius);
        double shift = double.PositiveInfinity;
        for (int pass = 0; pass < 3; pass++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            training = Sample(known.X, known.Y, 0, coreRadius, true);
            if (!Covered(training, .75, 100)) return Reject("標準球中心模型的訓練半圖觀測不足。");
            var next = EstimateKnownRadiusCap(training.Points, known, cancellationToken);
            if (next is null) return Reject("標準球中心模型未收斂。");
            shift = DistanceXY(next.X, next.Y, known.X, known.Y);
            known = next;
            if (DistanceXY(known.X, known.Y, seedX, seedY) > 5)
                return Reject("標準球中心偏離偵測位置超過 5 mm。");
            if (known.Z >= training.Points.Average(p => p.Z))
                return Reject("標準球中心模型不符合可見上半球。");
        }
        if (shift > Math.Max(.1, Math.Max(px, py) * .5)) return Reject("標準球中心未穩定。");

        // Freeze the validation aperture at the independent A-free model's XY.
        // Both models see identical B samples; no B sample was ever fitted, even
        // during recentering. Expected counts are the actual half-grid aperture.
        var core = Sample(free.X, free.Y, 0, coreRadius, false);
        var ring = Sample(free.X, free.Y, coreRadius, validationRadius, false);
        if (!Covered(core, .75, 100) || !Covered(ring, .65, 64))
            return Reject("獨立驗證半圖的核心或外圈覆蓋不足。");
        var knownCoreErrors = core.Points.Select(p => Math.Abs(Residual(p, known))).ToArray();
        var knownRingErrors = ring.Points.Select(p => Math.Abs(Residual(p, known))).ToArray();
        var freeCoreErrors = core.Points.Select(p => Math.Abs(Residual(p, free))).ToArray();
        var freeRingErrors = ring.Points.Select(p => Math.Abs(Residual(p, free))).ToArray();
        double knownCoreMse = MeanSquare(knownCoreErrors), knownRingMse = MeanSquare(knownRingErrors);
        double freeCoreMse = MeanSquare(freeCoreErrors), freeRingMse = MeanSquare(freeRingErrors);
        var knownTrainingErrors = training.Points.Select(p => Math.Abs(Residual(p, known))).ToArray();
        var differences = new double[ring.Points.Length];
        var sectorCounts = new int[8];
        var sectorSupport = new int[8];
        var sectorKnownLoss = new double[8];
        var sectorFreeLoss = new double[8];
        var sectorBias = new double[8];
        for (int i = 0; i < ring.Points.Length; i++)
        {
            if ((i & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
            var point = ring.Points[i];
            double knownZ = UpperSurfaceZ(known, point.X, point.Y);
            double freeZ = UpperSurfaceZ(free, point.X, point.Y);
            if (!double.IsFinite(knownZ) || !double.IsFinite(freeZ))
                return Reject("標準球與自由球面的上半球預測不相容。");
            double difference = knownZ - freeZ;
            differences[i] = Math.Abs(difference);
            int sector = Sector(point, free);
            sectorCounts[sector]++;
            if (knownRingErrors[i] <= .75) sectorSupport[sector]++;
            sectorKnownLoss[sector] += knownRingErrors[i] * knownRingErrors[i];
            sectorFreeLoss[sector] += freeRingErrors[i] * freeRingErrors[i];
            sectorBias[sector] += difference;
        }
        double maximumSectorExcess = 0, maximumSectorRmsDelta = 0, maximumSectorBias = 0;
        for (int i = 0; i < 8; i++)
        {
            if (sectorCounts[i] < Math.Max(8, ring.ExpectedSectorCounts[i] * .5)
                || sectorSupport[i] < sectorCounts[i] * .65)
                return Reject("獨立驗證半圖的外圈方向支持不足。");
            maximumSectorExcess = Math.Max(maximumSectorExcess,
                (sectorKnownLoss[i] - sectorFreeLoss[i]) / sectorCounts[i]);
            maximumSectorRmsDelta = Math.Max(maximumSectorRmsDelta,
                Math.Sqrt(sectorKnownLoss[i] / sectorCounts[i]) - Math.Sqrt(sectorFreeLoss[i] / sectorCounts[i]));
            maximumSectorBias = Math.Max(maximumSectorBias, Math.Abs(sectorBias[i] / sectorCounts[i]));
        }
        double knownCoreRms = Math.Sqrt(knownCoreMse), knownRingRms = Math.Sqrt(knownRingMse);
        result = result with
        {
            X = known.X, Y = known.Y, Z = known.Z, UsesKnownRadius = true,
            RecenterShiftMm = shift, TrainingPointCount = training.Points.Length,
            HoldoutPointCount = core.Points.Length + ring.Points.Length,
            HoldoutSectorPointCounts = sectorCounts,
            CoreRmsMm = knownCoreRms, CoreP95Mm = Quantile(knownCoreErrors, .95),
            ValidationRmsMm = knownRingRms, ValidationP95Mm = Quantile(knownRingErrors, .95),
            CorePointCount = core.Points.Length, ValidationPointCount = ring.Points.Length,
            InlierFraction = knownCoreErrors.Count(e => e <= .3) / (double)knownCoreErrors.Length,
            CoverageFraction = (core.Points.Length + ring.Points.Length) / (double)(core.ExpectedCount + ring.ExpectedCount),
            FreeComparisonCoreRmsMm = Math.Sqrt(freeCoreMse),
            FreeComparisonValidationRmsMm = Math.Sqrt(freeRingMse),
            ConstrainedVsFreeCoreRmsDeltaMm = knownCoreRms - Math.Sqrt(freeCoreMse),
            ConstrainedVsFreeValidationRmsDeltaMm = knownRingRms - Math.Sqrt(freeRingMse),
            CoreExcessMseMm2 = Math.Max(0, knownCoreMse - freeCoreMse),
            ValidationExcessMseMm2 = Math.Max(0, knownRingMse - freeRingMse),
            RelativeCoreExcessLoss = Math.Max(0, knownCoreMse - freeCoreMse) / Math.Max(freeCoreMse, .03 * .03),
            HoldoutPredictionP95DifferenceMm = Quantile(differences, .95),
            MaximumSectorExcessMseMm2 = maximumSectorExcess,
            MaximumSectorRmsDeltaMm = maximumSectorRmsDelta,
            MaximumSectorPredictionBiasMm = maximumSectorBias,
            BoundedCoreRmsMm = BoundedRms(knownCoreErrors),
            BoundedValidationRmsMm = BoundedRms(knownRingErrors),
            CoreGrossOutlierFraction = GrossOutlierFraction(knownCoreErrors),
            ValidationGrossOutlierFraction = GrossOutlierFraction(knownRingErrors),
            ValidationInlierFraction = knownRingErrors.Count(e => e <= .75) / (double)knownRingErrors.Length,
        };
        requiresLocalizedValidation |= !CoreQuality(knownCoreErrors) || !CoreQuality(knownTrainingErrors)
            || knownRingRms > .5 || result.ValidationP95Mm > 1;
        if (!BoundedCoreQuality(knownCoreErrors) || !BoundedCoreQuality(knownTrainingErrors))
            return Reject("標準球核心支持、有限污染品質或大誤差比例未通過。");
        // The annulus never enters either fitting objective. Its gross tail is
        // diagnostic, while independent support, bounded loss and model agreement
        // determine usability. The fitted core retains its separate 2% gross cap.
        if (result.ValidationInlierFraction < .85 || result.BoundedValidationRmsMm > .40)
            return Reject("標準球獨立外圈的支持或有限污染品質未通過。");
        if (Math.Abs(known.Z + known.Radius - Quantile(core.Points.Select(p => p.Z).ToArray(), .99)) > .8)
            return Reject("標準球模型的球頂與實測高度不一致。");
        // These absolute budgets are increases in measured RMS (mm). An MSE
        // difference also contains a cross-term with existing directional error
        // and would impose a noise-dependent, much smaller RMS allowance.
        if (result.ConstrainedVsFreeCoreRmsDeltaMm > .05 || result.RelativeCoreExcessLoss > .10
            || result.ConstrainedVsFreeValidationRmsDeltaMm > .10 || maximumSectorRmsDelta > .15
            || result.HoldoutPredictionP95DifferenceMm > .25 || maximumSectorBias > .25)
            return Reject("自由球面與標準球尺寸模型在獨立驗證點上的形狀不相容。");
        if (requiresLocalizedValidation)
        {
            // Remove each angular sector of the final A training aperture in
            // turn. No B point, residual-based subset, or selected diameter is
            // fitted. A stable center must survive loss of any one direction.
            double maximumShift = 0;
            for (int omitted = 0; omitted < 8; omitted++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var reduced = training.Points.Where(p => Sector(p, known) != omitted).ToArray();
                var perturbed = EstimateKnownRadiusCap(reduced, known, cancellationToken);
                if (perturbed is null) return Reject("移除單一訓練方向後標準球中心未收斂。");
                double delta = Math.Sqrt(Math.Pow(perturbed.X - known.X, 2)
                    + Math.Pow(perturbed.Y - known.Y, 2) + Math.Pow(perturbed.Z - known.Z, 2));
                maximumShift = Math.Max(maximumShift, delta);
            }
            result = result with { CenterSensitivityMm = maximumShift };
            if (maximumShift > Math.Max(.1, Math.Max(px, py)))
                return Reject("移除單一訓練方向後球心位移超過精度預算，局部污染救援未通過。");
        }
        return result with { Success = true, FailureReason = null,
            UsedLocalizedOutlierValidation = requiresLocalizedValidation };

        RobustSphereFitResult Reject(string reason) => result with { Success = false, FailureReason = reason };

        Aperture Sample(double x, double y, double inner, double outer, bool isTraining)
        {
            if (x - outer < ox - px * .5 || y - outer < oy - py * .5
                || x + outer > ox + (width - .5) * px || y + outer > oy + (height - .5) * py)
                return new([], 0, new int[8]);
            int x0 = Math.Max(0, (int)Math.Ceiling((x - outer - ox) / px));
            int x1 = Math.Min(width - 1, (int)Math.Floor((x + outer - ox) / px));
            int y0 = Math.Max(0, (int)Math.Ceiling((y - outer - oy) / py));
            int y1 = Math.Min(height - 1, (int)Math.Floor((y + outer - oy) / py));
            var points = new List<Point>();
            var expectedSectors = new int[8];
            int expected = 0;
            for (int row = y0; row <= y1; row++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                for (int col = x0; col <= x1; col++)
                {
                    if ((((gridX + col + gridY + row) & 1) == 0) != isTraining) continue;
                    double sx = ox + col * px, sy = oy + row * py;
                    double r2 = (sx - x) * (sx - x) + (sy - y) * (sy - y);
                    if (r2 > outer * outer || (inner > 0 && r2 <= inner * inner)) continue;
                    expected++;
                    expectedSectors[SectorXY(sx - x, sy - y)]++;
                    ushort raw = frame.Heights[row * width + col];
                    if (raw != 0) points.Add(new Point(sx, sy, (raw - 32768) * pz));
                }
            }
            return new(points.ToArray(), expected, expectedSectors);
        }
    }

    private static double DistanceXY(double x, double y, double xx, double yy) =>
        Math.Sqrt((x - xx) * (x - xx) + (y - yy) * (y - yy));
    private static double MeanSquare(double[] errors) => errors.Average(e => e * e);
    private static bool Covered(Aperture aperture, double fraction, int minimum) =>
        aperture.ExpectedCount > 0 && aperture.Points.Length >= minimum
        && aperture.Points.Length >= aperture.ExpectedCount * fraction;
    private static bool CoreQuality(double[] errors) => MeanSquare(errors) <= .35 * .35
        && Quantile(errors, .95) <= .8 && errors.Count(e => e <= .3) >= errors.Length * .8;
    // Fixed contamination budget for confirmed-standard centers only. All raw
    // errors remain in diagnostics and model-comparison checks. Capping quality
    // loss does not remove a point or change either fitting objective.
    private static double BoundedRms(double[] errors) =>
        Math.Sqrt(errors.Average(e => Math.Min(e, .75) * Math.Min(e, .75)));
    private static double GrossOutlierFraction(double[] errors) => errors.Count(e => e > 2) / (double)errors.Length;
    private static bool BoundedCoreQuality(double[] errors) => BoundedRms(errors) <= .30
        && errors.Count(e => e <= .3) >= errors.Length * .8 && GrossOutlierFraction(errors) <= .02;
    private sealed record Aperture(Point[] Points, int ExpectedCount, int[] ExpectedSectorCounts);

    private static bool AngularSupport(Point[] points, Estimate model, double inner, double outer, double minimumSectorCount)
    {
        var counts = new int[8];
        var supported = new int[8];
        foreach (var point in points)
        {
            double dx = point.X - model.X, dy = point.Y - model.Y;
            double r2 = dx * dx + dy * dy;
            if (r2 < inner * inner || r2 > outer * outer) continue;
            int sector = Math.Min(7, (int)((Math.Atan2(dy, dx) + Math.PI) / (2 * Math.PI) * 8));
            counts[sector]++;
            if (Math.Abs(Residual(point, model)) <= .75) supported[sector]++;
        }
        return Enumerable.Range(0, 8).All(i => counts[i] >= minimumSectorCount && supported[i] >= counts[i] * .65);
    }

    private static Estimate? EstimateCap(Point[] points, CancellationToken ct)
    {
        // Center the linear algebra; no nominal radius enters the estimation objective.
        double ox = points.Average(p => p.X), oy = points.Average(p => p.Y), oz = points.Average(p => p.Z);
        var local = points.Select(p => new Point(p.X - ox, p.Y - oy, p.Z - oz)).ToArray();
        var h = new double[4, 4];
        var g = new double[4];
        Span<double> row = stackalloc double[4];
        foreach (var point in local)
        {
            row[0] = 2 * point.X; row[1] = 2 * point.Y; row[2] = 2 * point.Z; row[3] = 1;
            double target = point.X * point.X + point.Y * point.Y + point.Z * point.Z;
            for (int i = 0; i < 4; i++)
            {
                g[i] += row[i] * target;
                for (int j = 0; j < 4; j++) h[i, j] += row[i] * row[j];
            }
        }
        if (!Solve(h, g, out var v)) return null;
        double radiusSquared = v[3] + v[0] * v[0] + v[1] * v[1] + v[2] * v[2];
        if (!double.IsFinite(radiusSquared) || radiusSquared <= 0) return null;
        v[3] = Math.Sqrt(radiusSquared);
        var errors = new double[local.Length];
        var weights = new double[local.Length];
        double damping = 1e-6;
        bool converged = false;
        for (int iteration = 0; iteration < 60; iteration++)
        {
            ct.ThrowIfCancellationRequested();
            for (int p = 0; p < local.Length; p++) errors[p] = Error(local[p], v);
            double median = Quantile(errors, .5);
            double scale = Math.Max(.03, 1.4826 * Quantile(errors.Select(e => Math.Abs(e - median)).ToArray(), .5));
            double cutoff = 1.345 * scale;
            Array.Clear(h); Array.Clear(g);
            double oldError = 0;
            for (int p = 0; p < local.Length; p++)
            {
                double error = errors[p];
                double weight = Math.Abs(error) <= cutoff ? 1 : cutoff / Math.Abs(error);
                weights[p] = weight;
                double dx = v[0] - local[p].X, dy = v[1] - local[p].Y, dz = v[2] - local[p].Z;
                double distance = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                if (distance < 1e-12) return null;
                row[0] = dx / distance; row[1] = dy / distance; row[2] = dz / distance; row[3] = -1;
                oldError += weight * error * error;
                for (int i = 0; i < 4; i++)
                {
                    g[i] -= weight * row[i] * error;
                    for (int j = 0; j < 4; j++) h[i, j] += weight * row[i] * row[j];
                }
            }
            bool accepted = false;
            double stepNorm = double.PositiveInfinity;
            double gradientNorm = Math.Sqrt(Enumerable.Range(0, 4).Sum(i => g[i] * g[i] / Math.Max(h[i, i], 1e-12)));
            if (gradientNorm <= 1e-6 * Math.Sqrt(local.Length))
            {
                converged = true;
                break;
            }
            for (int attempt = 0; attempt < 12; attempt++)
            {
                var damped = (double[,])h.Clone();
                for (int i = 0; i < 4; i++) damped[i, i] += damping * Math.Max(h[i, i], 1e-12);
                if (!Solve(damped, g, out var step)) return null;
                var trial = Enumerable.Range(0, 4).Select(i => v[i] + step[i]).ToArray();
                double nextError = 0;
                if (trial.All(double.IsFinite) && trial[3] > .05 && trial[3] < 1000)
                {
                    for (int p = 0; p < local.Length; p++)
                    {
                        double error = Error(local[p], trial);
                        nextError += weights[p] * error * error;
                    }
                }
                else nextError = double.PositiveInfinity;
                if (nextError <= oldError + 1e-12)
                {
                    v = trial;
                    stepNorm = step.Sum(d => d * d);
                    damping = Math.Max(1e-12, damping * .2);
                    accepted = true;
                    break;
                }
                damping *= 10;
            }
            if (!accepted) return null;
            if (stepNorm < 1e-10)
            {
                converged = true;
                break;
            }
        }
        return converged && v.All(double.IsFinite) && v[3] > .05 && v[3] < 1000
            ? new Estimate(v[0] + ox, v[1] + oy, v[2] + oz, v[3]) : null;
    }

    private static Estimate? EstimateKnownRadiusCap(Point[] points, Estimate initial, CancellationToken ct)
    {
        double ox = points.Average(p => p.X), oy = points.Average(p => p.Y), oz = points.Average(p => p.Z);
        var local = points.Select(p => new Point(p.X - ox, p.Y - oy, p.Z - oz)).ToArray();
        double[] center = [initial.X - ox, initial.Y - oy, initial.Z - oz];
        double radius = initial.Radius;
        var h = new double[3, 3];
        var g = new double[3];
        var errors = new double[local.Length];
        var weights = new double[local.Length];
        Span<double> row = stackalloc double[3];
        double damping = 1e-6;
        bool converged = false;
        for (int iteration = 0; iteration < 60; iteration++)
        {
            ct.ThrowIfCancellationRequested();
            for (int i = 0; i < local.Length; i++) errors[i] = KnownError(local[i], center, radius);
            double median = Quantile(errors, .5);
            double scale = Math.Max(.03, 1.4826 * Quantile(errors.Select(e => Math.Abs(e - median)).ToArray(), .5));
            double cutoff = 1.345 * scale;
            Array.Clear(h); Array.Clear(g);
            double oldError = 0;
            for (int p = 0; p < local.Length; p++)
            {
                double error = errors[p];
                double weight = Math.Abs(error) <= cutoff ? 1 : cutoff / Math.Abs(error);
                weights[p] = weight;
                double dx = center[0] - local[p].X, dy = center[1] - local[p].Y, dz = center[2] - local[p].Z;
                double distance = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                if (distance < 1e-12) return null;
                row[0] = dx / distance; row[1] = dy / distance; row[2] = dz / distance;
                oldError += weight * error * error;
                for (int i = 0; i < 3; i++)
                {
                    g[i] -= weight * row[i] * error;
                    for (int j = 0; j < 3; j++) h[i, j] += weight * row[i] * row[j];
                }
            }
            double gradientNorm = Math.Sqrt(Enumerable.Range(0, 3).Sum(i => g[i] * g[i] / Math.Max(h[i, i], 1e-12)));
            if (gradientNorm <= 1e-6 * Math.Sqrt(local.Length))
            {
                converged = true;
                break;
            }
            bool accepted = false;
            double stepNorm = double.PositiveInfinity;
            for (int attempt = 0; attempt < 12; attempt++)
            {
                var damped = (double[,])h.Clone();
                for (int i = 0; i < 3; i++) damped[i, i] += damping * Math.Max(h[i, i], 1e-12);
                if (!Solve(damped, g, out var step)) return null;
                var trial = Enumerable.Range(0, 3).Select(i => center[i] + step[i]).ToArray();
                double nextError = 0;
                if (!trial.All(double.IsFinite)) return null;
                for (int p = 0; p < local.Length; p++)
                {
                    double error = KnownError(local[p], trial, radius);
                    nextError += weights[p] * error * error;
                }
                if (nextError <= oldError + 1e-12)
                {
                    center = trial;
                    stepNorm = step.Sum(d => d * d);
                    damping = Math.Max(1e-12, damping * .2);
                    accepted = true;
                    break;
                }
                damping *= 10;
            }
            if (!accepted) return null;
            if (stepNorm < 1e-10 && gradientNorm <= 1e-4 * Math.Sqrt(local.Length))
            {
                converged = true;
                break;
            }
        }
        return converged && center.All(double.IsFinite)
            ? new Estimate(center[0] + ox, center[1] + oy, center[2] + oz, radius) : null;
    }

    private static bool Solve(double[,] matrix, double[] rhs, out double[] result)
    {
        int dimension = rhs.Length;
        var a = (double[,])matrix.Clone();
        var b = (double[])rhs.Clone();
        result = new double[dimension];
        for (int col = 0; col < dimension; col++)
        {
            int pivot = col;
            for (int row = col + 1; row < dimension; row++) if (Math.Abs(a[row, col]) > Math.Abs(a[pivot, col])) pivot = row;
            if (Math.Abs(a[pivot, col]) < 1e-12) return false;
            if (pivot != col)
            {
                for (int j = 0; j < dimension; j++) (a[col, j], a[pivot, j]) = (a[pivot, j], a[col, j]);
                (b[col], b[pivot]) = (b[pivot], b[col]);
            }
            double d = a[col, col];
            for (int j = col; j < dimension; j++) a[col, j] /= d;
            b[col] /= d;
            for (int row = 0; row < dimension; row++)
            {
                if (row == col) continue;
                double factor = a[row, col];
                for (int j = col; j < dimension; j++) a[row, j] -= factor * a[col, j];
                b[row] -= factor * b[col];
            }
        }
        result = b;
        return result.All(double.IsFinite);
    }

    private static double Error(Point p, double[] model) =>
        Math.Sqrt(Math.Pow(p.X - model[0], 2) + Math.Pow(p.Y - model[1], 2) + Math.Pow(p.Z - model[2], 2)) - model[3];
    private static double Residual(Point p, Estimate m) =>
        Math.Sqrt(Math.Pow(p.X - m.X, 2) + Math.Pow(p.Y - m.Y, 2) + Math.Pow(p.Z - m.Z, 2)) - m.Radius;
    private static double KnownError(Point p, double[] center, double radius) =>
        Math.Sqrt(Math.Pow(p.X - center[0], 2) + Math.Pow(p.Y - center[1], 2) + Math.Pow(p.Z - center[2], 2)) - radius;
    private static double UpperSurfaceZ(Estimate model, double x, double y)
    {
        double radicand = model.Radius * model.Radius - Math.Pow(x - model.X, 2) - Math.Pow(y - model.Y, 2);
        return radicand >= 0 ? model.Z + Math.Sqrt(radicand) : double.NaN;
    }
    private static int Sector(Point point, Estimate model) => SectorXY(point.X - model.X, point.Y - model.Y);
    private static int SectorXY(double dx, double dy) => Math.Min(7,
        (int)((Math.Atan2(dy, dx) + Math.PI) / (2 * Math.PI) * 8));
    private static double Quantile(double[] values, double q)
    {
        var sorted = (double[])values.Clone();
        Array.Sort(sorted);
        return sorted[(int)((sorted.Length - 1) * q)];
    }
    private static RobustSphereFitResult Failed(string reason) => new() { FailureReason = reason };
    private readonly record struct Point(double X, double Y, double Z);
    private sealed record Estimate(double X, double Y, double Z, double Radius);
}

/// <summary>Free measured diameter and either free or confirmed-standard center, with independent surface checks.</summary>
internal sealed record RobustSphereFitResult
{
    public bool Success { get; init; }
    /// <summary>All-pixel free fit passed geometry/coverage checks, independent of its diameter gate.</summary>
    public bool IsGeometryValid { get; init; }
    /// <summary>Original source fit passed finite data, coverage, convergence, drift, upper-cap and apex checks.</summary>
    public bool HasValidSourceSupport { get; init; }
    /// <summary>Untrimmed free quality failed; bounded A/B support and training-sector center sensitivity passed.</summary>
    public bool UsedLocalizedOutlierValidation { get; init; }
    /// <summary>Maximum A-only leave-one-sector-out center displacement; applicable when localized validation is used.</summary>
    public double CenterSensitivityMm { get; init; }
    public string? FailureReason { get; init; }
    public double X { get; init; }
    public double Y { get; init; }
    public double Z { get; init; }
    public double DiameterMm { get; init; }
    public bool UsesKnownRadius { get; init; }
    public double NominalDiameterMm { get; init; }
    public bool IsDiameterWithinTolerance { get; init; }
    public double FreeFitX { get; init; }
    public double FreeFitY { get; init; }
    public double FreeFitZ { get; init; }
    public double FullFreeCoreRmsMm { get; init; }
    public double FullFreeCoreP95Mm { get; init; }
    public double FullFreeValidationRmsMm { get; init; }
    public double FullFreeValidationP95Mm { get; init; }
    /// <summary>Independent free model fitted only to checkerboard training A pixels.</summary>
    public double ComparisonFreeDiameterMm { get; init; }
    public double ComparisonFreeX { get; init; }
    public double ComparisonFreeY { get; init; }
    public double ComparisonFreeZ { get; init; }
    public int TrainingPointCount { get; init; }
    public int HoldoutPointCount { get; init; }
    public int[] HoldoutSectorPointCounts { get; init; } = [];
    /// <summary>Both comparison RMS values below use untouched B pixels, not the diagnostic all-pixel model.</summary>
    public double FreeComparisonCoreRmsMm { get; init; }
    public double FreeComparisonValidationRmsMm { get; init; }
    public double ConstrainedVsFreeCoreRmsDeltaMm { get; init; }
    public double ConstrainedVsFreeValidationRmsDeltaMm { get; init; }
    public double CoreExcessMseMm2 { get; init; }
    public double ValidationExcessMseMm2 { get; init; }
    public double RelativeCoreExcessLoss { get; init; }
    public double HoldoutPredictionP95DifferenceMm { get; init; }
    public double MaximumSectorExcessMseMm2 { get; init; }
    /// <summary>Largest same-sector RMS increase in mm; MSE increase alone also depends on existing directional noise.</summary>
    public double MaximumSectorRmsDeltaMm { get; init; }
    public double MaximumSectorPredictionBiasMm { get; init; }
    public double CoreRmsMm { get; init; }
    public double CoreP95Mm { get; init; }
    public double ValidationRmsMm { get; init; }
    public double ValidationP95Mm { get; init; }
    public double BoundedCoreRmsMm { get; init; }
    public double BoundedValidationRmsMm { get; init; }
    public double CoreGrossOutlierFraction { get; init; }
    public double ValidationGrossOutlierFraction { get; init; }
    /// <summary>Fraction of untouched B annulus samples with absolute radial residual at most .75 mm.</summary>
    public double ValidationInlierFraction { get; init; }
    public double InlierFraction { get; init; }
    public double CoverageFraction { get; init; }
    public double RecenterShiftMm { get; init; }
    public int CorePointCount { get; init; }
    public int ValidationPointCount { get; init; }
}
