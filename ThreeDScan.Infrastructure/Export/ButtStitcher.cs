using ThreeDScan.Infrastructure.Export.Models;

namespace ThreeDScan.Infrastructure.Export;

/// <summary>無重疊端對端拼接。長向位置為明確的公稱間隙假設。</summary>
/// <remarks>以兩翼完整 XYZ 基底建立端部框；掃描器 Y 不代表工件長向。</remarks>
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
        log?.Invoke("沿實測兩翼交線擬合 A 尾端、B 頭端的兩翼板與長軸…");
        var fa = BuildFrame(cloudA, true, options, out var fitA);
        var fb = BuildFrame(cloudB, false, options, out var fitB);
        var aa = cloudA.Points.Select(p => fa.Align(p.Position)).ToArray();
        var bb = cloudB.Points.Select(p => fb.Align(p.Position)).ToArray();
        double DistanceA(int i)
        {
            return -fa.Align(cloudA.Points[i].Position).Y;
        }

        double DistanceB(int i)
        {
            return fb.Align(cloudB.Points[i].Position).Y;
        }

        int[] Select(ScanCloud cloud, Func<int, double> distance, double min, double max)
        {
            return Enumerable.Range(0, cloud.Points.Length)
                .Where(i => distance(i) >= min && distance(i) <= max).ToArray();
        }

        bool OnWing(EndFrame frame, ScanVector raw)
        {
            var local = frame.Align(raw);
            var plane = local.X < 0 ? frame.LeftSurface : frame.RightSurface;
            double distance = Math.Abs(local.Z - plane.A * local.X - plane.B * local.Y - plane.C)
                / Math.Sqrt(1 + plane.A * plane.A + plane.B * plane.B);
            return distance <= Math.Max(options.MinimumPlaneToleranceMm, 3 * plane.RmsMm);
        }
        var profileFitA = Select(cloudA, DistanceA, options.ProfileTrainingStartMm, options.ProfileTrainingEndMm)
            .Where(i => OnWing(fa, cloudA.Points[i].Position)).ToArray();
        var profileFitB = Select(cloudB, DistanceB, options.ProfileTrainingStartMm, options.ProfileTrainingEndMm)
            .Where(i => OnWing(fb, cloudB.Points[i].Position)).ToArray();
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

        // Each fitted frame is already located at its measured full-width cut.
        // Profile refinement acts only in X/Z and cannot change this station.
        const double cutA = 0, cutB = 0;
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

        var validationCandidatesA = Select(cloudA, DistanceA, 0, options.ValidationDepthMm).Where(i => keepA[i]).ToArray();
        var validationCandidatesB = Select(cloudB, DistanceB, 0, options.ValidationDepthMm).Where(i => keepB[i]).ToArray();
        var validationA = validationCandidatesA.Where(i => OnWing(fa, cloudA.Points[i].Position)).ToArray();
        var validationB = validationCandidatesB.Where(i => OnWing(fb, cloudB.Points[i].Position)).ToArray();
        double supportA = validationA.Length / (double)Math.Max(1, validationCandidatesA.Length);
        double supportB = validationB.Length / (double)Math.Max(1, validationCandidatesB.Length);
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
        var rawFullSection = StitchMath.CheckEdges(vpA, vpB, options);
        var edgeA = StitchMath.EdgeProfile(aa, validationA, options.ProfileBinMm, shift.X);
        var edgeB = StitchMath.EdgeProfile(bb, validationB, options.ProfileBinMm, shift.X + correction[0][2]);
        var fullSection = StitchMath.CheckEdges(edgeA, edgeB, options);
        // Both end profiles must also cover their own independently measured
        // interior wing width. Two similarly incomplete ends must not pass just
        // because they agree with one another after off-wing noise rejection.
        var interiorIndicesA = fitA.Where(i => OnWing(fa, cloudA.Points[i].Position)).ToArray();
        var interiorIndicesB = fitB.Where(i => OnWing(fb, cloudB.Points[i].Position)).ToArray();
        var interiorA = StitchMath.EdgeProfile(aa, interiorIndicesA, options.ProfileBinMm, shift.X);
        var interiorB = StitchMath.EdgeProfile(bb, interiorIndicesB, options.ProfileBinMm, shift.X + correction[0][2]);
        if (interiorA.Length < 30 || interiorB.Length < 30)
            throw new InvalidOperationException("內側兩翼截面不足，不能核對端部全寬。");
        var coverageA = StitchMath.CheckWidthCoverage(interiorA, edgeA, options);
        var coverageB = StitchMath.CheckWidthCoverage(interiorB, edgeB, options);
        var validation = new StitchValidation(
            rms <= options.MaximumValidationRmsMm && p95 <= options.MaximumValidationP95Mm && fullSection.Passed
                && coverageA.Passed && coverageB.Passed && supportA >= .7 && supportB >= .7,
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
            fullSection: fullSection)
        {
            UnfilteredFullSection = rawFullSection,
            LeftEndInteriorCoverage = coverageA,
            RightEndInteriorCoverage = coverageB,
            LeftEndWingSupportFraction = supportA,
            RightEndWingSupportFraction = supportB,
        };
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
            ValidationCandidatePointCount = validationCandidatesA.Length,
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
            ValidationCandidatePointCount = validationCandidatesB.Length,
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

    private static EndFrame BuildFrame(ScanCloud cloud, bool upper, ButtStitchOptions options, out int[] trainingIndices)
    {
        if (cloud.Points.Length < 1000)
        {
            throw new ArgumentException("每段至少需要 1000 個量測點。");
        }

        if (cloud.Points.Any(p => !double.IsFinite(p.Position.X) || !double.IsFinite(p.Position.Y) || !double.IsFinite(p.Position.Z)))
        {
            throw new ArgumentException("點雲包含無效座標。");
        }

        var points = cloud.Points.Select(p => p.Position).ToArray();
        var sourceIndices = Enumerable.Range(0, points.Length).ToArray();
        double maximumRms = Math.Max(options.MinimumPlaneToleranceMm, options.MaximumValidationRmsMm);
        // The coarse pair identifies the workpiece direction and the measured
        // end stations. Final plane parameters come only from the training band;
        // the near-end profile/validation bands remain excluded from that fit.
        var coarse = GeometryFitting.FitWings(points, sourceIndices, maximumRms);
        var supported = coarse.Left.Indices.Concat(coarse.Right.Indices).Distinct().ToArray();
        double Station(ScanVector p, WingFit basis) => (p - basis.RidgeOrigin).Dot(basis.LongAxis);
        double low = supported.Min(i => Station(points[i], coarse));
        double high = supported.Max(i => Station(points[i], coarse));
        var coarseBasis = SectionBasis(coarse, coarse.LongAxis);
        ScanVector CoarseAligned(ScanVector p)
        {
            var d = p - coarse.RidgeOrigin;
            return new ScanVector(d.Dot(coarseBasis.Cross), d.Dot(coarse.LongAxis), d.Dot(coarseBasis.Up));
        }
        // Every transverse bin contributes its own measured boundary. A global
        // scanner-coordinate extremum can be tens of mm ahead of the complete
        // section on an oblique scan and is not a valid held-out-band origin.
        double end = EndCut(supported.Select(i => CoarseAligned(points[i])).ToArray(), upper, options);
        double length = high - low;
        if (length < options.PlaneTrainingStartMm + options.PlaneTrainingDepthMm + 5)
        {
            throw new ArgumentException("掃描長度不足以分開擬合與驗證區域。");
        }

        trainingIndices = Enumerable.Range(0, points.Length).Where(i =>
        {
            double station = Station(points[i], coarse);
            double d = upper ? end - station : station - end;
            return d >= options.PlaneTrainingStartMm && d <= options.PlaneTrainingStartMm + options.PlaneTrainingDepthMm;
        }).ToArray();
        if (trainingIndices.Length < 500)
        {
            throw new InvalidOperationException($"{(upper ? "尾端" : "頭端")}內側 {options.PlaneTrainingStartMm:0.###}～{options.PlaneTrainingStartMm + options.PlaneTrainingDepthMm:0.###} mm 的兩翼擬合區只有 {trainingIndices.Length} 個量測點，至少需要 500 個；端部覆蓋不足。");
        }
        var training = trainingIndices.Select(i => points[i]).ToArray();
        var fit = GeometryFitting.FitWings(training, trainingIndices, maximumRms);
        // A pair's axis has no intrinsic head/tail sign. Keep the independently
        // measured coarse direction so a local band cannot reverse the segment.
        var axis = fit.LongAxis.Dot(coarse.LongAxis) >= 0 ? fit.LongAxis : fit.LongAxis.Scale(-1);
        var basis = SectionBasis(fit, axis);
        var cross = basis.Cross;
        var up = basis.Up;
        var finalAligned = supported.Select(i =>
        {
            var d = points[i] - fit.RidgeOrigin;
            return new ScanVector(d.Dot(cross), d.Dot(axis), d.Dot(up));
        }).ToArray();
        double finalEnd = EndCut(finalAligned, upper, options);
        var origin = fit.RidgeOrigin + axis.Scale(finalEnd);
        FittedSurface LocalSurface(PlaneFit plane)
        {
            double denominator = plane.Normal.Dot(up);
            if (Math.Abs(denominator) < 1e-6)
                throw new InvalidOperationException("端部翼板無法在截面基底表示。");
            return new FittedSurface(-plane.Normal.Dot(cross) / denominator,
                -plane.Normal.Dot(axis) / denominator, (plane.D - plane.Normal.Dot(origin)) / denominator,
                plane.Rms, plane.Indices.Length, training.Length);
        }
        var left = LocalSurface(basis.Left);
        var right = LocalSurface(basis.Right);
        return new EndFrame(origin, cross, axis, up, left, right, basis.Angle, origin.Y);
    }

    private static (ScanVector Cross, ScanVector Up, PlaneFit Left, PlaneFit Right, double Angle) SectionBasis(WingFit fit, ScanVector axis)
    {
        ScanVector WingRay(PlaneFit plane)
        {
            var direction = plane.Normal.Cross(axis).Normalize();
            double station = StitchMath.Median(plane.Indices.Select(i => (fit.Points[i] - fit.RidgeOrigin).Dot(direction)));
            if (Math.Abs(station) < 1e-9)
                throw new InvalidOperationException("端部翼板缺少可辨識的稜線外延方向。");
            return direction.Scale(station > 0 ? 1 : -1);
        }
        var rayA = WingRay(fit.Left);
        var rayB = WingRay(fit.Right);
        // Surface normal Z cannot choose the outward side of a rolled or
        // vertical L. Observed wing rays give a single-valued convex section.
        var outward = (rayA + rayB).Normalize().Scale(-1);
        var cross = axis.Cross(outward).Normalize();
        var up = cross.Cross(axis).Normalize();
        if (rayA.Dot(up) >= -1e-6 || rayB.Dot(up) >= -1e-6 || rayA.Dot(cross) * rayB.Dot(cross) >= 0)
            throw new InvalidOperationException("端部實測兩翼不支持同一凸折面截面。");
        var left = rayA.Dot(cross) < 0 ? fit.Left : fit.Right;
        var right = rayA.Dot(cross) < 0 ? fit.Right : fit.Left;
        var nl = left.Normal.Dot(up) >= 0 ? left.Normal : left.Normal.Scale(-1);
        var nr = right.Normal.Dot(up) >= 0 ? right.Normal : right.Normal.Scale(-1);
        double angle = Math.Acos(Math.Clamp(nl.Dot(nr), -1, 1)) * 180 / Math.PI;
        return (cross, up, left, right, angle);
    }

    private static double EndCut(ScanVector[] points, bool upper, ButtStitchOptions options)
    {
        var columns = points.GroupBy(p => StitchMath.TransverseBin(p.X, options.ProfileBinMm))
            .Where(g => g.Count() >= 3).Select(g =>
            {
                double low = g.Min(p => p.Y), high = g.Max(p => p.Y);
                double endpoint = upper ? high : low;
                return new
                {
                    U = StitchMath.Median(g.Select(p => p.X)),
                    Span = high - low,
                    Endpoint = endpoint,
                    Support = g.Count(p => Math.Abs(p.Y - endpoint) <= options.EndBandMm),
                    Count = g.Count(),
                };
            }).Where(c => c.Support >= 3).ToArray();
        if (columns.Length < 30)
        {
            throw new InvalidOperationException("掃描端部截面覆蓋不足。");
        }

        double quantile = upper ? options.EndCoverageQuantile : 1 - options.EndCoverageQuantile;
        double originalEnd = StitchMath.Quantile(columns.Select(c => c.Endpoint), quantile);
        double center = StitchMath.Median(columns.Select(c => c.Endpoint));
        double typicalSpan = StitchMath.Median(columns.Select(c => c.Span));
        double Displacement(double endpoint, double reference) => upper ? reference - endpoint : endpoint - reference;
        // Leave the original cut exactly unchanged when it agrees with the
        // main end-point population. Ordinary short edge columns and oblique
        // measured end faces must not be discarded merely for sparse support.
        if (Displacement(originalEnd, center) <= Math.Max(options.EndBandMm, .5 * typicalSpan))
            return originalEnd;

        var left = columns.Where(c => c.U < 0).ToArray();
        var right = columns.Where(c => c.U >= 0).ToArray();
        double leftCenter = left.Length == 0 ? center : StitchMath.Median(left.Select(c => c.Endpoint));
        double rightCenter = right.Length == 0 ? center : StitchMath.Median(right.Select(c => c.Endpoint));
        double leftSpan = left.Length == 0 ? typicalSpan : StitchMath.Median(left.Select(c => c.Span));
        double rightSpan = right.Length == 0 ? typicalSpan : StitchMath.Median(right.Select(c => c.Span));
        // Only remove the specific misleading evidence: a bin confined wholly
        // to one end-band whose endpoint is near the opposite end of its wing.
        // Density is irrelevant; even a dense local patch is not a full-length
        // edge. A sparse column spanning the part remains eligible unchanged.
        var eligible = columns.Where(c => !(c.Support == c.Count && c.Span <= options.EndBandMm
            && Displacement(c.Endpoint, c.U < 0 ? leftCenter : rightCenter)
                > Math.Max(options.EndBandMm, .5 * (c.U < 0 ? leftSpan : rightSpan)))).ToArray();
        if (eligible.Length < 30)
        {
            throw new InvalidOperationException("沿長向持續觀測的端部兩翼截面覆蓋不足。");
        }

        return StitchMath.Quantile(eligible.Select(c => c.Endpoint), quantile);
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
