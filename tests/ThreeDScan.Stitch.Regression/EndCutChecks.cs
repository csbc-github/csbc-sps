using System.Reflection;
using ThreeDScan.Infrastructure.Export;
using ThreeDScan.Infrastructure.Export.Models;

internal static class EndCutChecks
{
    internal static void Run(Action<string, Func<object>> check)
    {
        check("narrow-head-rejects-seven-tail-only-fringe-columns", () => ClusteredFringe(false, false, 55));
        check("narrow-tail-rejects-seven-head-only-fringe-columns", () => ClusteredFringe(true, false, 55));
        check("narrow-head-rejects-apex-and-outer-tail-fringes", () => ClusteredFringe(false, true, 55));
        check("dense-local-tail-patch-does-not-define-head", () => ClusteredFringe(false, false, 1000));
        check("short-fringe-near-the-real-end-preserves-original-conservative-cut", NearEndShortFringe);
        check("sparse-complete-wing-keeps-independent-end-stations", SparseWing);
        check("three-separated-points-do-not-bypass-end-band-support", ThreePointGate);
        check("isolated-longitudinal-outlier-does-not-invalidate-all-columns", IsolatedOutlier);
        check("oblique-physical-end-face-retains-known-cut-position", ObliqueEnd);
        check("narrow-XYZ-pair-with-tail-only-fringe-stitches-at-real-head", NarrowPair);
    }

    private static double Cut(ScanVector[] points, bool upper)
    {
        var method = typeof(ButtStitcher).GetMethod("EndCut", BindingFlags.Static | BindingFlags.NonPublic)!;
        try { return (double)method.Invoke(null, [points, upper, new ButtStitchOptions()])!; }
        catch (TargetInvocationException error) when (error.InnerException is not null) { throw error.InnerException; }
    }

    private static ScanVector[] Band(Func<double, IEnumerable<double>>? stations = null)
    {
        var points = new List<ScanVector>();
        for (int column = 0; column < 260; column++)
        {
            double x = -64.75 + .5 * column;
            foreach (double y in stations?.Invoke(x) ?? Enumerable.Range(0, 301).Select(i => (double)i))
                points.Add(new(x, y, -Math.Abs(x)));
        }
        return points.ToArray();
    }

    private static object ClusteredFringe(bool upper, bool apex, int pointsPerColumn)
    {
        var body = Band().Where(p => !apex || p.X is not (> 0 and < 1)).ToList();
        double[] fringeX = apex ? [0.25, .75, 65.25, 65.75, 66.25, 66.75, 67.25]
            : Enumerable.Range(0, 7).Select(i => 65.25 + .5 * i).ToArray();
        foreach (double x in fringeX)
            for (int i = 0; i < pointsPerColumn; i++)
            {
                double y = (upper ? 0 : 290) + 10d * i / (pointsPerColumn - 1);
                body.Add(new(x, y, -Math.Abs(x)));
            }
        var input = body.ToArray();
        var before = (ScanVector[])input.Clone();
        double cut = Cut(input, upper), expected = upper ? 300 : 0;
        Assert(Math.Abs(cut - expected) < 1e-8,
            $"Seven columns observed only at the opposite end moved the known {(upper ? "tail" : "head")} from {expected} to {cut:F6} mm.");
        Assert(input.SequenceEqual(before), "End location mutated source measurements.");
        return new { upper, oppositeEndOnlyColumns = 7, pointsPerColumn, clusteredSpanMm = 10, measuredLengthMm = 300, cutMm = cut, sourceUnchanged = true };
    }

    private static object SparseWing()
    {
        // Both wings have known physical end faces. One wing is much sparser,
        // but has the existing minimum three measurements in each 12mm end band
        // and independent middle samples. The other wing must not set its density.
        var input = Band(x => x < 0 ? [12d, 13, 14, 150, 286, 287, 288]
            : Enumerable.Range(0, 301).Select(i => (double)i));
        double head = Cut(input, false), tail = Cut(input, true);
        Assert(Math.Abs(head - 12) < 1e-8 && Math.Abs(tail - 288) < 1e-8,
            $"Sparse but independently repeated wing was dropped: head {head}, tail {tail}; expected 12/288 mm.");
        return new { sparseSamplesPerColumn = 7, denseSamplesPerColumn = 301, headMm = head, tailMm = tail };
    }

    private static object NearEndShortFringe()
    {
        // These short columns lie beside the actual end, not at its opposite
        // end. The original fully-covered cut is deliberately conservative by
        // 8mm. A general density/span filter must not move that accepted cut.
        ScanVector[] WithFringe(double start)
        {
            var points = Band().ToList();
            for (int column = 0; column < 7; column++)
                for (int sample = 0; sample < 55; sample++)
                {
                    double x = 65.25 + .5 * column, y = start + 10d * sample / 54;
                    points.Add(new(x, y, -Math.Abs(x)));
                }
            return points.ToArray();
        }
        var headInput = WithFringe(8);
        var tailInput = WithFringe(282);
        var beforeHead = (ScanVector[])headInput.Clone();
        var beforeTail = (ScanVector[])tailInput.Clone();
        double head = Cut(headInput, false), tail = Cut(tailInput, true);
        Assert(Math.Abs(head - 8) < 1e-8 && Math.Abs(tail - 292) < 1e-8,
            $"A near-end short fringe changed the original conservative cuts 8/292mm to {head:F6}/{tail:F6}; only opposite-end mislocations may trigger the fallback.");
        Assert(headInput.SequenceEqual(beforeHead) && tailInput.SequenceEqual(beforeTail), "Conservative end-location checks changed original measurements.");
        return new { mainEndStationsMm = new[] { 0, 300 }, shortColumnSpanMm = 10,
            originalConservativeHeadMm = 8, originalConservativeTailMm = 292, headMm = head, tailMm = tail,
            normalOriginalCutsPreservedExactly = true, sourceUnchanged = true };
    }

    private static object ThreePointGate()
    {
        var input = Band(_ => [0d, 150, 300]);
        bool rejected = false;
        try { Cut(input, false); }
        catch (InvalidOperationException) { rejected = true; }
        Assert(rejected, "Three samples across the entire scan must not bypass the existing three samples in the end-band gate.");
        return new { totalSamplesPerColumn = 3, endpointSamplesPerColumn = 1, rejected };
    }

    private static object IsolatedOutlier()
    {
        var input = Band().Concat([new ScanVector(.25, 900, -.25)]).ToArray();
        double head = Cut(input, false), tail = Cut(input, true);
        Assert(Math.Abs(head) < 1e-8 && Math.Abs(tail - 300) < 1e-8,
            "One isolated far-longitudinal measurement invalidated the independently complete 300mm wing bands.");
        return new { physicalLengthMm = 300, isolatedPointYmm = 900, headMm = head, tailMm = tail };
    }

    private static object ObliqueEnd()
    {
        var input = Band(x => Enumerable.Range(0, 301).Select(i => i + .25 * x));
        // The independent plane y=.25*x is scanned on x=-64.75..64.75;
        // its 98% fully covered head and 2% tail station are analytic values.
        const double expectedHead = -16.1875 + 32.375 * .98;
        const double expectedTail = 300 - 16.1875 + 32.375 * .02;
        double head = Cut(input, false), tail = Cut(input, true);
        Assert(Math.Abs(head - expectedHead) < 1e-8 && Math.Abs(tail - expectedTail) < 1e-8,
            $"A real oblique physical cut was replaced by a guessed square end: {head:F6}/{tail:F6}.");
        return new { physicalEndSlope = .25, headMm = head, tailMm = tail, expectedHead, expectedTail };
    }

    private static object NarrowPair()
    {
        var a = Cloud(35, 17, 31, new(350, 470, 80), false);
        var b = Cloud(-25, -9, 12, new(420, 510, 95), true);
        var snapshotA = a.Cloud.Points.Select(p => (p.Position, p.ExtraColumns)).ToArray();
        var snapshotB = b.Cloud.Points.Select(p => (p.Position, p.ExtraColumns)).ToArray();
        var pair = ButtStitcher.Run(a.Cloud, b.Cloud);
        double tailA = (pair.PartA.Frame.Origin - a.Origin).Dot(a.Axis);
        double headB = (pair.PartB.Frame.Origin - b.Origin).Dot(b.Axis);
        Assert(Math.Abs(tailA - 300) < .05 && Math.Abs(headB) < .05,
            $"Known physical end planes were misplaced: first tail {tailA:F6}, second head {headB:F6} mm.");
        Assert(pair.Validation.Passed && pair.Validation.FitAndValidationIndicesDisjoint,
            "Independent narrow 90-degree wings did not pass unchanged held-out seam quality.");
        foreach (var part in new[] { pair.PartA, pair.PartB })
        {
            Assert(part.PlaneTrainingIndices.Length >= 500, "The corrected known end did not have its independently available training band.");
            Assert(!part.PlaneTrainingIndices.Intersect(part.ValidationIndices).Any()
                && !part.ProfileTrainingIndices.Intersect(part.ValidationIndices).Any(), "End correction leaked fitting points into held-out validation.");
        }
        Assert(a.Cloud.Points.Select(p => (p.Position, p.ExtraColumns)).SequenceEqual(snapshotA)
            && b.Cloud.Points.Select(p => (p.Position, p.ExtraColumns)).SequenceEqual(snapshotB), "Pair stitching modified original points or attributes.");
        return new { firstTailMm = tailA, secondHeadMm = headB, pair.Validation,
            firstTrainingPoints = pair.PartA.PlaneTrainingIndices.Length, secondTrainingPoints = pair.PartB.PlaneTrainingIndices.Length, sourceUnchanged = true };
    }

    private static (ScanCloud Cloud, ScanVector Axis, ScanVector Origin) Cloud(double yaw, double pitch, double roll, ScanVector origin, bool tailFringe)
    {
        ScanVector Rotate(ScanVector p)
        {
            double a = yaw * Math.PI / 180, b = pitch * Math.PI / 180, c = roll * Math.PI / 180;
            var q = new ScanVector(p.X, Math.Cos(b) * p.Y - Math.Sin(b) * p.Z, Math.Sin(b) * p.Y + Math.Cos(b) * p.Z);
            var r = new ScanVector(Math.Cos(c) * q.X + Math.Sin(c) * q.Z, q.Y, -Math.Sin(c) * q.X + Math.Cos(c) * q.Z);
            return new(Math.Cos(a) * r.X - Math.Sin(a) * r.Y, Math.Sin(a) * r.X + Math.Cos(a) * r.Y, r.Z);
        }
        var local = new List<ScanVector>();
        for (int y = 0; y <= 300; y++)
            for (int x = -65; x <= 65; x++) local.Add(new(x, y, -Math.Abs(x)));
        if (tailFringe)
            for (int column = 0; column < 7; column++)
                for (int row = 0; row < 45; row++)
                {
                    double x = 65.5 + .5 * column;
                    local.Add(new(x, 290 + 10d * row / 44, -x));
                }
        var points = local.Select((p, i) => new SourceScanPoint(i, origin + Rotate(p), $"{i % 256} {i}")).ToArray();
        string[] header = ["ply", "format ascii 1.0", "element vertex 0", "property float x", "property float y", "property float z",
            "property uchar intensity", "property int raw_point_index", "end_header"];
        return (new(header, points), Rotate(new(0, 1, 0)), origin);
    }

    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
