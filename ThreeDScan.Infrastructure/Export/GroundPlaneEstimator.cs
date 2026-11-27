using ThreeDScan.Infrastructure.Export.Models;

namespace ThreeDScan.Infrastructure.Export;

/// <summary>
/// Finds a spatially supported lower plane in scanner XY. Object surfaces cannot
/// win merely by containing more points than the exposed surrounding table.
/// </summary>
internal static class GroundPlaneEstimator
{
    private const int GridSize = 24;
    private const int Iterations = 768;

    internal sealed record EstimateResult(
        double A, double B, double C, double Rmse,
        int[] CornerCounts, ScanBounds Bounds, GroundPlaneDiagnostics Diagnostics);

    private readonly record struct Sample(ScanVector Point, int X, int Y)
    {
        internal bool Peripheral => X < 3 || X >= GridSize - 3 || Y < 3 || Y >= GridSize - 3;
    }

    private readonly record struct Plane(double A, double B, double C)
    {
        internal double Scale => Math.Sqrt(A * A + B * B + 1);
        internal double SignedDistance(ScanVector p) => (p.Z - A * p.X - B * p.Y - C) / Scale;
    }

    private readonly record struct Evidence(
        int Support, int PeripheralSupport, int Below, double SpanX, double SpanY, int Quadrants, double Score);

    internal static EstimateResult Estimate(
        SourceScanPoint[] source, ExtractionOptions options, CancellationToken cancellationToken)
    {
        // One jittered sample per source-index block avoids aliasing organized
        // scan rows, while bounding all fitting/sorting work independently of N.
        int stride = Math.Max(1, (int)Math.Ceiling(source.Length / (double)options.GroundMaxSamples));
        var sampled = new List<ScanVector>(Math.Min(source.Length, options.GroundMaxSamples));
        var random = new Random(0x3D5CA);
        for (int start = 0; start < source.Length; start += stride)
        {
            if ((sampled.Count & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
            int length = Math.Min(stride, source.Length - start);
            sampled.Add(source[start + (length == 1 ? 0 : random.Next(length))].Position);
        }

        if (sampled.Count < 30)
            throw new CalibrationMathException("地面取樣不足 30 點，不能可靠分離工件與地面。");

        double minX = Quantile(sampled.Select(p => p.X), .002), maxX = Quantile(sampled.Select(p => p.X), .998);
        double minY = Quantile(sampled.Select(p => p.Y), .002), maxY = Quantile(sampled.Select(p => p.Y), .998);
        double width = maxX - minX, lengthY = maxY - minY;
        if (width < 1 || lengthY < 1)
            throw new CalibrationMathException("地面 XY 覆蓋不足，不能可靠擬合平面。");

        var bins = new List<ScanVector>[GridSize * GridSize];
        var samples = new List<Sample>(sampled.Count);
        var cornerCounts = new int[4];
        foreach (var p in sampled)
        {
            if (p.X < minX || p.X > maxX || p.Y < minY || p.Y > maxY) continue;
            int x = Math.Min(GridSize - 1, (int)((p.X - minX) / width * GridSize));
            int y = Math.Min(GridSize - 1, (int)((p.Y - minY) / lengthY * GridSize));
            (bins[y * GridSize + x] ??= new List<ScanVector>()).Add(p);
            samples.Add(new Sample(p, x, y));
            if (x < 3 && y < 3) cornerCounts[0]++;
            if (x >= GridSize - 3 && y < 3) cornerCounts[1]++;
            if (x < 3 && y >= GridSize - 3) cornerCounts[2]++;
            if (x >= GridSize - 3 && y >= GridSize - 3) cornerCounts[3]++;
        }

        // Give occupied spatial regions equal weight. A dense raised top face
        // therefore cannot outvote a less densely sampled exposed perimeter.
        var representatives = new List<Sample>();
        for (int cell = 0; cell < bins.Length; cell++)
        {
            var bin = bins[cell];
            if (bin is null || bin.Count < 3) continue;
            bin.Sort((a, b) => a.Z.CompareTo(b.Z));
            representatives.Add(new Sample(bin[(int)((bin.Count - 1) * .2)], cell % GridSize, cell / GridSize));
        }
        var peripheral = representatives.Where(s => s.Peripheral).ToArray();
        if (representatives.Count < 9 || peripheral.Length < 4)
            throw new CalibrationMathException("地面缺少分散的周邊取樣區塊，不能可靠分離工件與地面。");

        Plane? best = null;
        double bestScore = double.NegativeInfinity;
        int hypotheses = 0;
        for (int iteration = 0; iteration < Iterations; iteration++)
        {
            if ((iteration & 31) == 0) cancellationToken.ThrowIfCancellationRequested();
            // Half the hypotheses start at the perimeter; the others cover
            // the complete lower envelope, including partially occluded beds.
            IReadOnlyList<Sample> pool = iteration % 2 == 0 ? peripheral : representatives;
            var p = pool[random.Next(pool.Count)].Point;
            var q = pool[random.Next(pool.Count)].Point;
            var r = pool[random.Next(pool.Count)].Point;
            if (!TryPlane(p, q, r, width * lengthY * .005, options.GroundMaximumSlope, out var plane)) continue;
            hypotheses++;
            var evidence = Evaluate(plane, representatives, peripheral.Length, options.GroundDistanceToleranceMm);
            if (!Supported(evidence, representatives.Count, peripheral.Length, options)) continue;
            if (evidence.Score <= bestScore) continue;
            best = plane;
            bestScore = evidence.Score;
        }

        if (best is null)
            throw new CalibrationMathException("找不到跨多區塊且有周邊支持的地面平面；請保留可見地面或調整拍攝範圍。");

        var fitted = best.Value;
        var inliers = new List<ScanVector>();
        for (int pass = 0; pass < 4; pass++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            inliers.Clear();
            foreach (var sample in samples)
                if (Math.Abs(fitted.SignedDistance(sample.Point)) <= options.GroundDistanceToleranceMm) inliers.Add(sample.Point);
            if (!TryLeastSquares(inliers, out var next) || Math.Sqrt(next.A * next.A + next.B * next.B) > options.GroundMaximumSlope)
                throw new CalibrationMathException("地面支持點退化，無法可靠擬合傾斜平面。");
            fitted = next;
        }

        var finalEvidence = Evaluate(fitted, representatives, peripheral.Length, options.GroundDistanceToleranceMm);
        inliers.Clear();
        int below = 0;
        double residualSquares = 0;
        foreach (var sample in samples)
        {
            double distance = fitted.SignedDistance(sample.Point);
            if (distance < -3 * options.GroundDistanceToleranceMm) below++;
            if (Math.Abs(distance) > options.GroundDistanceToleranceMm) continue;
            inliers.Add(sample.Point);
            residualSquares += distance * distance;
        }
        double rmse = Math.Sqrt(residualSquares / Math.Max(1, inliers.Count));
        double supportFraction = inliers.Count / (double)samples.Count;
        double belowFraction = below / (double)samples.Count;
        if (!Supported(finalEvidence, representatives.Count, peripheral.Length, options)
            || supportFraction < options.GroundMinimumSupportFraction
            || belowFraction > .10 || rmse > options.GroundDistanceToleranceMm * .8)
            throw new CalibrationMathException($"地面平面支持不足或有較低平面歧義（支持 {supportFraction:P1}、下方點 {belowFraction:P1}、RMS {rmse:F3} mm），未套用高度備援。");

        var diagnostics = new GroundPlaneDiagnostics
        {
            SampleCount = samples.Count,
            OccupiedRegionCount = representatives.Count,
            SupportingRegionCount = finalEvidence.Support,
            PeripheralRegionCount = peripheral.Length,
            SupportingPeripheralRegionCount = finalEvidence.PeripheralSupport,
            SupportPointCount = inliers.Count,
            SupportFraction = supportFraction,
            BelowPlaneFraction = belowFraction,
            CoverageXFraction = finalEvidence.SpanX,
            CoverageYFraction = finalEvidence.SpanY,
            PeripheralSupportFraction = finalEvidence.PeripheralSupport / (double)peripheral.Length,
            HypothesisCount = hypotheses,
            Reliable = true,
            Warning = supportFraction < .25 ? "可見地面支持比例較低，請核對周邊地面與工件遮擋。" : null,
        };
        return new EstimateResult(fitted.A, fitted.B, fitted.C, rmse, cornerCounts,
            ScanBounds.Of(source.Select(p => p.Position)), diagnostics);
    }

    private static bool Supported(Evidence e, int regions, int peripheral, ExtractionOptions options) =>
        e.Support >= Math.Max(6, regions * options.GroundMinimumSupportFraction)
        && e.PeripheralSupport >= Math.Max(4, peripheral * .20)
        && e.SpanX >= .45 && e.SpanY >= .45 && e.Quadrants >= 3;

    private static Evidence Evaluate(Plane plane, IReadOnlyList<Sample> regions, int peripheral, double tolerance)
    {
        int support = 0, edge = 0, below = 0, quadrants = 0;
        int minX = GridSize, maxX = -1, minY = GridSize, maxY = -1;
        foreach (var region in regions)
        {
            double distance = plane.SignedDistance(region.Point);
            if (distance < -3 * tolerance) below++;
            if (Math.Abs(distance) > tolerance) continue;
            support++;
            if (region.Peripheral) edge++;
            minX = Math.Min(minX, region.X); maxX = Math.Max(maxX, region.X);
            minY = Math.Min(minY, region.Y); maxY = Math.Max(maxY, region.Y);
            quadrants |= 1 << ((region.X >= GridSize / 2 ? 1 : 0) + (region.Y >= GridSize / 2 ? 2 : 0));
        }
        double spanX = (maxX - minX + 1) / (double)GridSize, spanY = (maxY - minY + 1) / (double)GridSize;
        double score = (support / (double)regions.Count + 2 * edge / (double)peripheral)
            * Math.Sqrt(Math.Max(0, spanX * spanY)) - 2 * below / (double)regions.Count;
        return new Evidence(support, edge, below, spanX, spanY, System.Numerics.BitOperations.PopCount((uint)quadrants), score);
    }

    private static bool TryPlane(ScanVector p, ScanVector q, ScanVector r, double minimumArea,
        double maximumSlope, out Plane plane)
    {
        var n = (q - p).Cross(r - p);
        if (Math.Abs(n.Z) < minimumArea) { plane = default; return false; }
        double a = -n.X / n.Z, b = -n.Y / n.Z, c = p.Z - a * p.X - b * p.Y;
        plane = new Plane(a, b, c);
        return double.IsFinite(a) && double.IsFinite(b) && double.IsFinite(c) && Math.Sqrt(a * a + b * b) <= maximumSlope;
    }

    private static bool TryLeastSquares(List<ScanVector> points, out Plane plane)
    {
        plane = default;
        if (points.Count < 10) return false;
        double mx = 0, my = 0, mz = 0;
        foreach (var p in points) { mx += p.X; my += p.Y; mz += p.Z; }
        mx /= points.Count; my /= points.Count; mz /= points.Count;
        double xx = 0, yy = 0, xy = 0, xz = 0, yz = 0;
        foreach (var p in points)
        {
            double x = p.X - mx, y = p.Y - my, z = p.Z - mz;
            xx += x * x; yy += y * y; xy += x * y; xz += x * z; yz += y * z;
        }
        double denominator = xx * yy - xy * xy;
        if (Math.Abs(denominator) < 1e-10 * Math.Max(1, xx * yy)) return false;
        double a = (xz * yy - yz * xy) / denominator, b = (yz * xx - xz * xy) / denominator;
        plane = new Plane(a, b, mz - a * mx - b * my);
        return double.IsFinite(plane.A) && double.IsFinite(plane.B) && double.IsFinite(plane.C);
    }

    private static double Quantile(IEnumerable<double> values, double q)
    {
        var ordered = values.Order().ToArray();
        double position = (ordered.Length - 1) * q;
        int first = (int)position, second = Math.Min(first + 1, ordered.Length - 1);
        return ordered[first] + (ordered[second] - ordered[first]) * (position - first);
    }
}
