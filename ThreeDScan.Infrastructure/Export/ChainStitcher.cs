using ThreeDScan.Infrastructure.Export.Models;

namespace ThreeDScan.Infrastructure.Export;

/// <summary>兩段以上原始掃描器點雲的有序無重疊拼接。</summary>
/// <remarks>由 Hank Scan3D 的 ChainStitcher 搬入，邏輯不改。</remarks>
public static class ChainStitcher
{
    /// <summary>由檔案路徑拼接。</summary>
    /// <remarks>來自 Hank Scan3D 的 <c>Scan3D.Processing.Legacy.Stitching.ChainStitcher</c>。</remarks>
    /// <param name="files">PLY 路徑。</param>
    /// <param name="options">參數。</param>
    /// <param name="log">進度紀錄。</param>
    public static ChainStitchResult Run(IReadOnlyList<string> files, ButtStitchOptions? options = null, Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(files);
        if (files.Count < 2)
        {
            throw new ArgumentException("請至少選擇兩份角鋼 PLY。");
        }

        string[] paths = files.Select(Path.GetFullPath).ToArray();
        if (paths.Distinct(StringComparer.OrdinalIgnoreCase).Count() != paths.Length)
        {
            throw new ArgumentException("同一份 PLY 不能重複加入拼接序列。");
        }

        var clouds = new ScanCloud[paths.Length];
        for (int i = 0; i < paths.Length; i++)
        {
            log?.Invoke($"讀取第 {i + 1}/{paths.Length} 段：{Path.GetFileName(paths[i])}");
            try
            {
                clouds[i] = ScanPly.Read(paths[i]);
            }
            catch (IOException ex)
            {
                throw new IOException($"第 {i + 1} 段讀取失敗：{ex.Message}", ex);
            }
        }

        return Run(clouds, options, log);
    }

    /// <summary>由點雲拼接。</summary>
    /// <remarks>來自 Hank Scan3D 的 <c>Scan3D.Processing.Legacy.Stitching.ChainStitcher</c>。</remarks>
    /// <param name="clouds">點雲。</param>
    /// <param name="options">參數。</param>
    /// <param name="log">進度紀錄。</param>
    public static ChainStitchResult Run(IReadOnlyList<ScanCloud> clouds, ButtStitchOptions? options = null, Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(clouds);
        if (clouds.Count < 2)
        {
            throw new ArgumentException("拼接至少需要兩段點雲。");
        }

        options ??= new ButtStitchOptions();
        var pairs = new ButtStitchResult[clouds.Count - 1];
        for (int i = 0; i < pairs.Length; i++)
        {
            log?.Invoke($"處理接縫 {i + 1}/{pairs.Length}：第 {i + 1} 段 → 第 {i + 2} 段");
            try
            {
                pairs[i] = ButtStitcher.Run(clouds[i], clouds[i + 1], options);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                throw new InvalidOperationException($"接縫 {i + 1}（第 {i + 1} → {i + 2} 段）失敗：{ex.Message}", ex);
            }
        }

        var poses = new StitchPose[clouds.Count];
        var keep = clouds.Select(cloud => Enumerable.Repeat(true, cloud.Points.Length).ToArray()).ToArray();
        poses[0] = pairs[0].PartA.Pose;
        for (int i = 0; i < pairs.Length; i++)
        {
            // Both sides of an interior segment constrain the same original point indices.
            for (int j = 0; j < keep[i].Length; j++)
            {
                keep[i][j] &= pairs[i].PartA.Retained[j];
            }

            for (int j = 0; j < keep[i + 1].Length; j++)
            {
                keep[i + 1][j] &= pairs[i].PartB.Retained[j];
            }

            var localToGlobal = poses[i].Compose(pairs[i].PartA.Pose.Inverse());
            poses[i + 1] = localToGlobal.Compose(pairs[i].PartB.Pose);
        }

        // Preserve the original two-input pipeline exactly, including six-decimal rounding.
        if (clouds.Count == 2)
        {
            var pair = pairs[0];
            var identity = new StitchPose(new ScanVector(1, 0, 0), new ScanVector(0, 1, 0), new ScanVector(0, 0, 1), new ScanVector());
            return new ChainStitchResult
            {
                Options = options,
                Bounds = pair.Bounds,
                LegacyPair = pair,
                Parts =
                [
                    Part(0, pair.PartA.Source, pair.PartA.Pose, pair.PartA.TransformedPoints, pair.PartA.Retained),
                    Part(1, pair.PartB.Source, pair.PartB.Pose, pair.PartB.TransformedPoints, pair.PartB.Retained),
                ],
                Seams =
                [
                    new ChainSeam
                    {
                        SeamNumber = 1,
                        LeftEnd = pair.PartA,
                        RightEnd = pair.PartB,
                        LocalToGlobalPose = identity,
                        Validation = pair.Validation,
                        ValidationProfile = pair.ValidationProfile,
                    },
                ],
            };
        }

        if (keep.Any(mask => mask.Count(value => value) < 1000))
        {
            throw new InvalidOperationException("中間段兩端裁切後有效點不足，無法進行可靠拼接。");
        }

        var transformed = clouds.Select((cloud, i) => cloud.Points.Select(p => poses[i].Apply(p.Position)).ToArray()).ToArray();
        var preliminaryBounds = ScanBounds.Of(transformed.SelectMany((points, i) => points.Where((_, j) => keep[i][j])));
        var shift = new ScanVector(-preliminaryBounds.MinX, -preliminaryBounds.MinY, -preliminaryBounds.MinZ);
        for (int i = 0; i < clouds.Count; i++)
        {
            poses[i] = new StitchPose(poses[i].RowX, poses[i].RowY, poses[i].RowZ, poses[i].Translation + shift);
            for (int j = 0; j < transformed[i].Length; j++)
            {
                transformed[i][j] += shift;
            }
        }

        var parts = clouds.Select((cloud, i) => Part(i, cloud, poses[i], transformed[i], keep[i])).ToArray();
        var seams = new ChainSeam[pairs.Length];
        for (int i = 0; i < pairs.Length; i++)
        {
            var pair = pairs[i];
            int[] a = pair.PartA.ValidationIndices.Where(j => keep[i][j]).ToArray();
            int[] b = pair.PartB.ValidationIndices.Where(j => keep[i + 1][j]).ToArray();
            var comparisons = Compare(pair, a, b, options, out var fullSection);
            var absolute = comparisons.Select(p => Math.Abs(p.DeltaMm)).ToArray();
            double rms = Math.Sqrt(comparisons.Average(p => p.DeltaMm * p.DeltaMm));
            double p95 = StitchMath.Quantile(absolute, .95);
            var validation = new StitchValidation(
                rms <= options.MaximumValidationRmsMm && p95 <= options.MaximumValidationP95Mm && fullSection.Passed,
                comparisons.Length,
                rms,
                StitchMath.Median(absolute),
                p95,
                absolute.Max(),
                comparisons[^1].U - comparisons[0].U,
                pair.PartB.TransformedPoints.Where((_, j) => keep[i + 1][j]).Min(p => p.Y)
                    - pair.PartA.TransformedPoints.Where((_, j) => keep[i][j]).Max(p => p.Y),
                pair.Validation.FlangeAngleDifferenceDegrees,
                pair.Validation.NominalGapMm,
                pair.Validation.SeamYmm,
                pair.Validation.FitAndValidationIndicesDisjoint,
                pair.Validation.Method,
                pair.Validation.AbsoluteLongitudinalPlacementVerified,
                pair.Validation.PhysicalOverallLengthVerified,
                fullSection);
            seams[i] = new ChainSeam
            {
                SeamNumber = i + 1,
                LeftEnd = End(pair.PartA, a),
                RightEnd = End(pair.PartB, b),
                LocalToGlobalPose = poses[i].Compose(pair.PartA.Pose.Inverse()),
                Validation = validation,
                ValidationProfile = comparisons,
            };
            log?.Invoke($"接縫 {i + 1}：RMS {rms:F3} mm，P95 {p95:F3} mm，{(validation.Passed ? "通過" : "未通過")}");
        }

        return new ChainStitchResult
        {
            Parts = parts,
            Seams = seams,
            Options = options,
            Bounds = ScanBounds.Of(parts.SelectMany(part => part.TransformedPoints.Where((_, j) => part.Retained[j]))),
        };
    }

    private static ChainPart Part(int i, ScanCloud source, StitchPose pose, ScanVector[] points, bool[] kept)
    {
        return new ChainPart
        {
            ScanIndex = i,
            Source = source,
            Pose = pose,
            TransformedPoints = points,
            Retained = kept,
        };
    }

    private static StitchedPart End(StitchedPart part, int[] validation)
    {
        return new StitchedPart
        {
            Source = part.Source,
            Frame = part.Frame,
            Pose = part.Pose,
            CutCoordinateMm = part.CutCoordinateMm,
            Retained = part.Retained,
            PlaneTrainingIndices = part.PlaneTrainingIndices,
            ProfileTrainingIndices = part.ProfileTrainingIndices,
            ValidationIndices = validation,
        };
    }

    private static ProfileComparison[] Compare(ButtStitchResult pair, int[] a, int[] b, ButtStitchOptions options, out SectionEdgeCheck fullSection)
    {
        var pa = StitchMath.Profile(pair.PartA.TransformedPoints, a, options.ProfileBinMm);
        var pb = StitchMath.Profile(pair.PartB.TransformedPoints, b, options.ProfileBinMm);
        if (pa.Length < 30 || pb.Length < 30)
        {
            throw new InvalidOperationException("多段裁切後留出的接縫驗證截面不足。");
        }

        fullSection = StitchMath.CheckEdges(pa, pb, options);
        double low = Math.Max(pa[0].U, pb[0].U) + options.ProfileEdgeMarginMm;
        double high = Math.Min(pa[^1].U, pb[^1].U) - options.ProfileEdgeMarginMm;
        var samples = new List<ProfileComparison>();
        for (double u = low; u < high; u += options.ProfileBinMm)
        {
            double? va = StitchMath.Interpolate(pa, u), vb = StitchMath.Interpolate(pb, u);
            if (va.HasValue && vb.HasValue)
            {
                samples.Add(new ProfileComparison(u, va.Value, vb.Value, va.Value - vb.Value));
            }
        }

        if (samples.Count < 30)
        {
            throw new InvalidOperationException("多段裁切後沒有足夠的共同有效截面。");
        }

        return samples.ToArray();
    }
}
