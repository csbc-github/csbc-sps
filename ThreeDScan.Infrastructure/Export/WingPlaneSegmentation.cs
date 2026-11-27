using ThreeDScan.Infrastructure.Export.Models;

namespace ThreeDScan.Infrastructure.Export;

/// <summary>以有界取樣分離實測平面；不判定平面是否為角鋼翼板。</summary>
internal static class WingPlaneSegmentation
{
    private const int SampleLimit = 6000;
    private const int HypothesisCount = 160;
    private const int MinimumFullSupport = 100;

    /// <summary>傳回的支持索引直接指向輸入點；不旋轉、平移或新增量測點。</summary>
    internal static PlaneFit[] FindPlanes(
        ScanVector[] points,
        double tolerance,
        int maxPlanes = 3,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (!double.IsFinite(tolerance) || tolerance <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tolerance));
        }
        if (maxPlanes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxPlanes));
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (maxPlanes == 0 || points.Length < MinimumFullSupport)
        {
            return [];
        }

        var explained = new bool[points.Length];
        var planes = new List<PlaneFit>(Math.Min(maxPlanes, 8));
        int remaining = points.Length;
        var random = new StableRandom(0xC4A1B892D73506EFUL ^ (ulong)points.Length);
        // A duplicate fringe may be consumed without using a returned-plane slot.
        long extractionLimit = (long)maxPlanes * 2 + 2;
        for (long extraction = 0; extraction < extractionLimit && planes.Count < maxPlanes
            && remaining >= MinimumFullSupport; extraction++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sample = SampleRemaining(points, explained, remaining, ref random, cancellationToken);
            int minimumSampleSupport = Math.Max(30, (int)Math.Ceiling(sample.Length * .015));
            var reference = new ScanVector(sample.Average(p => p.X), sample.Average(p => p.Y), sample.Average(p => p.Z));
            int bestCount = 0;
            double bestSquaredError = double.PositiveInfinity;
            ScanVector bestNormal = default;
            double bestD = 0;

            for (int hypothesis = 0; hypothesis < HypothesisCount; hypothesis++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int a = random.Next(sample.Length);
                int b = random.Next(sample.Length - 1);
                if (b >= a) b++;
                int c = random.Next(sample.Length - 2);
                int low = Math.Min(a, b), high = Math.Max(a, b);
                if (c >= low) c++;
                if (c >= high) c++;

                var ab = sample[b] - sample[a];
                var ac = sample[c] - sample[a];
                var cross = ab.Cross(ac);
                double areaSquared = cross.Dot(cross);
                double scaleSquared = ab.Dot(ab) * ac.Dot(ac);
                if (!double.IsFinite(areaSquared) || scaleSquared <= 1e-12
                    || areaSquared <= scaleSquared * 1e-10)
                {
                    continue;
                }

                var normal = cross.Scale(1 / Math.Sqrt(areaSquared));
                if (normal.Z < 0) normal = normal.Scale(-1);
                double d = normal.Dot(sample[a]);
                if (IsDuplicate(normal, d, reference, planes, tolerance)) continue;
                int support = 0;
                double squaredError = 0;
                foreach (var point in sample)
                {
                    double residual = normal.Dot(point) - d;
                    if (Math.Abs(residual) <= tolerance)
                    {
                        support++;
                        squaredError += residual * residual;
                    }
                }
                if (support > bestCount || support == bestCount && squaredError < bestSquaredError)
                {
                    bestCount = support;
                    bestSquaredError = squaredError;
                    bestNormal = normal;
                    bestD = d;
                }
            }

            if (bestCount < minimumSampleSupport) break;
            int[] inliers = Enumerable.Range(0, sample.Length)
                .Where(i => Math.Abs(bestNormal.Dot(sample[i]) - bestD) <= tolerance).ToArray();
            PlaneFit refined;
            try
            {
                refined = GeometryFitting.Plane(sample, inliers, minimumPoints: 30);
            }
            catch (InvalidOperationException)
            {
                // A nearly collinear consensus does not establish a measured surface.
                break;
            }
            cancellationToken.ThrowIfCancellationRequested();

            int refinedSampleSupport = 0;
            foreach (var point in sample)
            {
                if (Math.Abs(refined.Normal.Dot(point) - refined.D) <= tolerance) refinedSampleSupport++;
            }
            if (refinedSampleSupport < minimumSampleSupport) break;

            var indices = new List<int>();
            double fullSquaredError = 0;
            for (int i = 0; i < points.Length; i++)
            {
                if ((i & 16383) == 0) cancellationToken.ThrowIfCancellationRequested();
                if (explained[i]) continue;
                double residual = refined.Normal.Dot(points[i]) - refined.D;
                if (Math.Abs(residual) <= tolerance)
                {
                    indices.Add(i);
                    fullSquaredError += residual * residual;
                }
            }
            if (indices.Count < MinimumFullSupport) break;

            bool duplicate = IsDuplicate(refined.Normal, refined.D, reference, planes, tolerance);
            foreach (int index in indices) explained[index] = true;
            remaining -= indices.Count;
            if (!duplicate)
            {
                planes.Add(new PlaneFit
                {
                    Normal = refined.Normal,
                    D = refined.D,
                    Rms = Math.Sqrt(fullSquaredError / indices.Count),
                    Indices = indices.ToArray(),
                });
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        return planes.ToArray();
    }

    private static ScanVector[] SampleRemaining(
        ScanVector[] points, bool[] explained, int remaining, ref StableRandom random, CancellationToken cancellationToken)
    {
        var sample = new ScanVector[Math.Min(SampleLimit, remaining)];
        int seen = 0;
        for (int i = 0; i < points.Length; i++)
        {
            if ((i & 16383) == 0) cancellationToken.ThrowIfCancellationRequested();
            if (explained[i]) continue;
            if (seen < sample.Length)
            {
                sample[seen] = points[i];
            }
            else
            {
                int slot = random.Next(seen + 1);
                if (slot < sample.Length) sample[slot] = points[i];
            }
            seen++;
        }
        return sample;
    }

    private static bool IsDuplicate(
        ScanVector normal, double d, ScanVector reference, List<PlaneFit> planes, double tolerance)
    {
        foreach (var plane in planes)
        {
            double alignment = normal.Dot(plane.Normal);
            if (Math.Abs(alignment) < .9995) continue;
            double sign = alignment < 0 ? -1 : 1;
            // Compare offsets at the same local point; D alone depends on the world origin.
            double separation = Math.Abs(normal.Dot(reference) - d
                - sign * (plane.Normal.Dot(reference) - plane.D));
            if (separation <= tolerance * 2) return true;
        }
        return false;
    }

    private struct StableRandom(ulong seed)
    {
        private ulong _state = seed;

        internal int Next(int exclusiveMaximum)
        {
            // SplitMix64 is fixed here so results do not depend on System.Random versions.
            unchecked
            {
                ulong value = (_state += 0x9E3779B97F4A7C15UL);
                value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
                value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
                value ^= value >> 31;
                return (int)(value % (ulong)exclusiveMaximum);
            }
        }
    }
}
