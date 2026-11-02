using ThreeDScan.Infrastructure.Export.Models;

namespace ThreeDScan.Infrastructure.Export;

/// <summary>無重疊端對端拼接。長向位置為明確的公稱間隙假設。</summary>
/// <remarks>由 Hank Scan3D 的 ButtStitcher 搬入，邏輯不改。</remarks>
public static class ButtStitcher
{
    /// <summary>由兩份 PLY 拼接。</summary>
    /// <remarks>來自 Hank Scan3D 的 <c>Scan3D.Processing.Legacy.Stitching.ButtStitcher</c>。</remarks>
    /// <param name="fileA">A 段 PLY。</param>
    /// <param name="fileB">B 段 PLY。</param>
    /// <param name="options">參數。</param>
    /// <param name="log">進度紀錄。</param>
    public static ButtStitchResult Run(string fileA, string fileB, ButtStitchOptions? options = null, Action<string>? log = null)
    {
        return Run(ScanPly.Read(fileA), ScanPly.Read(fileB), options, log);
    }

    /// <summary>由兩段點雲拼接。</summary>
    /// <remarks>來自 Hank Scan3D 的 <c>Scan3D.Processing.Legacy.Stitching.ButtStitcher</c>。</remarks>
    /// <param name="cloudA">A 段。</param>
    /// <param name="cloudB">B 段。</param>
    /// <param name="options">參數。</param>
    /// <param name="log">進度紀錄。</param>
    public static ButtStitchResult Run(ScanCloud cloudA, ScanCloud cloudB, ButtStitchOptions? options = null, Action<string>? log = null)
    {
        options ??= new ButtStitchOptions();
        ValidateOptions(options);
        log?.Invoke("擬合 A 最大 Y 端、B 最小 Y 端的兩翼板與長軸…");
        var fa = BuildFrame(cloudA, true, options);
        var fb = BuildFrame(cloudB, false, options);
        var aa = cloudA.Points.Select(p => fa.Align(p.Position)).ToArray();
        var bb = cloudB.Points.Select(p => fb.Align(p.Position)).ToArray();
        double DistanceA(int i)
        {
            return fa.EndpointYmm - cloudA.Points[i].Position.Y;
        }

        double DistanceB(int i)
        {
            return cloudB.Points[i].Position.Y - fb.EndpointYmm;
        }

        int[] Select(ScanCloud cloud, Func<int, double> distance, double min, double max)
        {
            return Enumerable.Range(0, cloud.Points.Length)
                .Where(i => distance(i) >= min && distance(i) <= max).ToArray();
        }

        var fitA = Select(cloudA, DistanceA, options.PlaneTrainingStartMm, options.PlaneTrainingStartMm + options.PlaneTrainingDepthMm);
        var fitB = Select(cloudB, DistanceB, options.PlaneTrainingStartMm, options.PlaneTrainingStartMm + options.PlaneTrainingDepthMm);
        var profileFitA = Select(cloudA, DistanceA, options.ProfileTrainingStartMm, options.ProfileTrainingEndMm);
        var profileFitB = Select(cloudB, DistanceB, options.ProfileTrainingStartMm, options.ProfileTrainingEndMm);
        log?.Invoke("用獨立的內側截面帶微調截面旋轉與平移…");
        var correction = StitchMath.RefineProfile(
            StitchMath.Profile(aa, profileFitA, options.ProfileBinMm),
            StitchMath.Profile(bb, profileFitB, options.ProfileBinMm),
            options.ProfileEdgeMarginMm);
        for (int i = 0; i < bb.Length; i++)
        {
            var p = bb[i];
            bb[i] = new ScanVector(
                correction[0][0] * p.X + correction[0][1] * p.Z + correction[0][2],
                p.Y,
                correction[1][0] * p.X + correction[1][1] * p.Z + correction[1][2]);
        }

        double cutA = EndCut(aa, true, options), cutB = EndCut(bb, false, options);
        var keepA = aa.Select(p => p.Y <= cutA).ToArray();
        var keepB = bb.Select(p => p.Y >= cutB).ToArray();
        for (int i = 0; i < aa.Length; i++)
        {
            aa[i] = new ScanVector(aa[i].X, aa[i].Y - cutA, aa[i].Z);
        }

        for (int i = 0; i < bb.Length; i++)
        {
            bb[i] = new ScanVector(bb[i].X, bb[i].Y - cutB + options.NominalGapMm, bb[i].Z);
        }

        var preBounds = ScanBounds.Of(aa.Where((_, i) => keepA[i]).Concat(bb.Where((_, i) => keepB[i])));
        var shift = new ScanVector(-preBounds.MinX, -preBounds.MinY, -preBounds.MinZ);
        for (int i = 0; i < aa.Length; i++)
        {
            aa[i] += shift;
        }

        for (int i = 0; i < bb.Length; i++)
        {
            bb[i] += shift;
        }

        var validationA = Select(cloudA, DistanceA, 0, options.ValidationDepthMm).Where(i => keepA[i]).ToArray();
        var validationB = Select(cloudB, DistanceB, 0, options.ValidationDepthMm).Where(i => keepB[i]).ToArray();
        var vpA = StitchMath.Profile(aa, validationA, options.ProfileBinMm);
        var vpB = StitchMath.Profile(bb, validationB, options.ProfileBinMm);
        if (vpA.Length < 30 || vpB.Length < 30)
        {
            throw new InvalidOperationException("留出的驗證截面不足，無法評估接縫。");
        }

        double minU = Math.Max(vpA.Min(p => p.U), vpB.Min(p => p.U)) + options.ProfileEdgeMarginMm;
        double maxU = Math.Min(vpA.Max(p => p.U), vpB.Max(p => p.U)) - options.ProfileEdgeMarginMm;
        var comparisons = new List<ProfileComparison>();
        for (double u = minU; u < maxU; u += options.ProfileBinMm)
        {
            double? a = StitchMath.Interpolate(vpA, u), b = StitchMath.Interpolate(vpB, u);
            if (a.HasValue && b.HasValue)
            {
                comparisons.Add(new ProfileComparison(u, a.Value, b.Value, a.Value - b.Value));
            }
        }

        if (comparisons.Count < 30)
        {
            throw new InvalidOperationException("兩段沒有足夠的共同有效截面。");
        }

        double rms = Math.Sqrt(comparisons.Average(p => p.DeltaMm * p.DeltaMm));
        var abs = comparisons.Select(p => Math.Abs(p.DeltaMm)).ToArray();
        double p95 = StitchMath.Quantile(abs, .95);
        bool disjoint = !validationA.Intersect(fitA.Concat(profileFitA)).Any() && !validationB.Intersect(fitB.Concat(profileFitB)).Any();
        var fullSection = StitchMath.CheckEdges(vpA, vpB, options);
        var validation = new StitchValidation(
            rms <= options.MaximumValidationRmsMm && p95 <= options.MaximumValidationP95Mm && fullSection.Passed,
            comparisons.Count,
            rms,
            StitchMath.Median(abs),
            p95,
            abs.Max(),
            comparisons[^1].U - comparisons[0].U,
            bb.Where((_, i) => keepB[i]).Min(p => p.Y) - aa.Where((_, i) => keepA[i]).Max(p => p.Y),
            Math.Abs(fa.PlaneNormalAngleDegrees - fb.PlaneNormalAngleDegrees),
            options.NominalGapMm,
            shift.Y,
            disjoint,
            fullSection: fullSection);
        if (!disjoint)
        {
            throw new InvalidOperationException("擬合資料與驗證資料不可重疊。");
        }

        var poseA = Pose(fa, [[1, 0, 0], [0, 1, 0], [0, 0, 1]], new ScanVector(shift.X, shift.Y - cutA, shift.Z));
        var poseB = Pose(fb, correction, new ScanVector(shift.X, shift.Y - cutB + options.NominalGapMm, shift.Z));
        var partA = new StitchedPart
        {
            Source = cloudA,
            TransformedPoints = aa,
            Retained = keepA,
            Frame = fa,
            Pose = poseA,
            CutCoordinateMm = cutA,
            PlaneTrainingIndices = fitA,
            ProfileTrainingIndices = profileFitA,
            ValidationIndices = validationA,
        };
        var partB = new StitchedPart
        {
            Source = cloudB,
            TransformedPoints = bb,
            Retained = keepB,
            Frame = fb,
            Pose = poseB,
            CutCoordinateMm = cutB,
            PlaneTrainingIndices = fitB,
            ProfileTrainingIndices = profileFitB,
            ValidationIndices = validationB,
        };
        log?.Invoke($"截面檢查：RMS {rms:F3} mm，P95 {p95:F3} mm；保留 {partA.RetainedPointCount + partB.RetainedPointCount:N0} 點。");
        return new ButtStitchResult
        {
            PartA = partA,
            PartB = partB,
            Options = options,
            Validation = validation,
            ValidationProfile = comparisons.ToArray(),
            Bounds = ScanBounds.Of(aa.Where((_, i) => keepA[i]).Concat(bb.Where((_, i) => keepB[i]))),
            ProfileCorrection2D = correction,
        };
    }

    private static StitchPose Pose(EndFrame frame, double[][] correction, ScanVector shift)
    {
        var x = frame.CrossAxis.Scale(correction[0][0]) + frame.UpAxis.Scale(correction[0][1]);
        var z = frame.CrossAxis.Scale(correction[1][0]) + frame.UpAxis.Scale(correction[1][1]);
        return new StitchPose(
            x,
            frame.LongAxis,
            z,
            new ScanVector(
                -x.Dot(frame.Origin) + correction[0][2] + shift.X,
                -frame.LongAxis.Dot(frame.Origin) + shift.Y,
                -z.Dot(frame.Origin) + correction[1][2] + shift.Z));
    }

    private static EndFrame BuildFrame(ScanCloud cloud, bool upper, ButtStitchOptions options)
    {
        if (cloud.Points.Length < 1000)
        {
            throw new ArgumentException("每段至少需要 1000 個量測點。");
        }

        if (cloud.Points.Any(p => !double.IsFinite(p.Position.X) || !double.IsFinite(p.Position.Y) || !double.IsFinite(p.Position.Z)))
        {
            throw new ArgumentException("點雲包含無效座標。");
        }

        double end = upper ? cloud.Points.Max(p => p.Position.Y) : cloud.Points.Min(p => p.Position.Y);
        double length = cloud.Points.Max(p => p.Position.Y) - cloud.Points.Min(p => p.Position.Y);
        if (length < options.PlaneTrainingStartMm + options.PlaneTrainingDepthMm + 5)
        {
            throw new ArgumentException("掃描長度不足以分開擬合與驗證區域。");
        }

        var train = cloud.Points.Where(p =>
        {
            double d = upper ? end - p.Position.Y : p.Position.Y - end;
            return d >= options.PlaneTrainingStartMm && d <= options.PlaneTrainingStartMm + options.PlaneTrainingDepthMm;
        }).Select(p => p.Position).ToArray();
        var peaks = train.GroupBy(p => p.Y).ToDictionary(g => g.Key, g => g.MaxBy(p => p.Z).X);
        var left = StitchMath.FitPlane(train.Where(p => p.X < peaks[p.Y] - options.ApexExclusionMm).ToArray(), options.MinimumPlaneToleranceMm);
        var right = StitchMath.FitPlane(train.Where(p => p.X > peaks[p.Y] + options.ApexExclusionMm).ToArray(), options.MinimumPlaneToleranceMm);
        double denominator = left.A - right.A;
        if (Math.Abs(denominator) < .1)
        {
            throw new InvalidOperationException("未辨識到兩個不同的角鋼翼板。");
        }

        double dx = -(left.B - right.B) / denominator, intercept = -(left.C - right.C) / denominator;
        double x = dx * end + intercept, z = left.A * x + left.B * end + left.C;
        var axis = new ScanVector(dx, 1, left.A * dx + left.B).Normalize();
        var nl = new ScanVector(-left.A, -left.B, 1).Normalize();
        var nr = new ScanVector(-right.A, -right.B, 1).Normalize();
        var up = (nl + nr).Normalize();
        var cross = axis.Cross(up).Normalize();
        up = cross.Cross(axis).Normalize();
        double angle = Math.Acos(Math.Clamp(nl.Dot(nr), -1, 1)) * 180 / Math.PI;
        if (angle < 30 || angle > 150)
        {
            throw new InvalidOperationException("翼板夾角不符合可用的角鋼截面。");
        }

        return new EndFrame(new ScanVector(x, end, z), cross, axis, up, left, right, angle, end);
    }

    private static double EndCut(ScanVector[] points, bool upper, ButtStitchOptions options)
    {
        var edges = points.Where(p => Math.Abs(p.Y) < options.EndBandMm).GroupBy(p => (long)Math.Floor(p.X / options.ProfileBinMm))
            .Where(g => g.Count() >= 3).Select(g => upper ? g.Max(p => p.Y) : g.Min(p => p.Y)).ToArray();
        if (edges.Length < 30)
        {
            throw new InvalidOperationException("掃描端部截面覆蓋不足。");
        }

        return StitchMath.Quantile(edges, upper ? options.EndCoverageQuantile : 1 - options.EndCoverageQuantile);
    }

    private static void ValidateOptions(ButtStitchOptions o)
    {
        double[] values =
        [
            o.NominalGapMm, o.PlaneTrainingStartMm, o.PlaneTrainingDepthMm, o.ProfileTrainingStartMm, o.ProfileTrainingEndMm,
            o.ValidationDepthMm, o.ProfileBinMm, o.ProfileEdgeMarginMm, o.ApexExclusionMm, o.MinimumPlaneToleranceMm, o.EndBandMm,
            o.EndCoverageQuantile, o.MaximumValidationRmsMm, o.MaximumValidationP95Mm, o.MaximumSectionEdgeDistanceMm, o.MinimumSectionCoverageRatio,
        ];
        if (values.Any(v => !double.IsFinite(v)) || o.NominalGapMm < 0 || o.ProfileBinMm <= 0 || o.ProfileBinMm > 2
            || o.ValidationDepthMm <= 0 || o.ProfileTrainingStartMm <= o.ValidationDepthMm || o.ProfileTrainingEndMm <= o.ProfileTrainingStartMm
            || o.PlaneTrainingStartMm <= o.ProfileTrainingEndMm || o.PlaneTrainingDepthMm <= 0 || o.EndCoverageQuantile < 0 || o.EndCoverageQuantile > .1
            || o.MinimumPlaneToleranceMm <= 0 || o.ProfileEdgeMarginMm < 0 || o.ApexExclusionMm <= 0 || o.EndBandMm <= 0
            || o.MaximumValidationRmsMm <= 0 || o.MaximumValidationP95Mm <= 0 || o.MaximumSectionEdgeDistanceMm <= 0
            || o.MinimumSectionCoverageRatio <= 0 || o.MinimumSectionCoverageRatio > 1)
        {
            throw new ArgumentException("拼接參數無效，擬合、微調及驗證區域必須分開。");
        }
    }
}
