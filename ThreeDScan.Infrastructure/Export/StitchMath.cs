using ThreeDScan.Infrastructure.Export.Models;

namespace ThreeDScan.Infrastructure.Export;

/// <summary>拼接用平面、截面與線性代數。</summary>
/// <remarks>由 Hank Scan3D 的 StitchMath 搬入，邏輯不改。</remarks>
internal static class StitchMath
{
    /// <summary>檢查兩截面全寬邊緣。</summary>
    /// <remarks>來自 Hank Scan3D 的 <c>Scan3D.Processing.Legacy.Stitching.StitchMath</c>。</remarks>
    /// <param name="a">截面 A。</param>
    /// <param name="b">截面 B。</param>
    /// <param name="options">參數。</param>
    internal static SectionEdgeCheck CheckEdges(ProfilePoint[] a, ProfilePoint[] b, ButtStitchOptions options)
    {
        double Distance(ProfilePoint p, ProfilePoint q)
        {
            return Math.Sqrt((p.U - q.U) * (p.U - q.U) + (p.Z - q.Z) * (p.Z - q.Z));
        }

        double left = Distance(a[0], b[0]), right = Distance(a[^1], b[^1]);
        double widthA = a[^1].U - a[0].U, widthB = b[^1].U - b[0].U;
        double coverage = Math.Max(0, Math.Min(a[^1].U, b[^1].U) - Math.Max(a[0].U, b[0].U)) / Math.Max(widthA, widthB);
        return new SectionEdgeCheck(
            left <= options.MaximumSectionEdgeDistanceMm && right <= options.MaximumSectionEdgeDistanceMm
            && coverage >= options.MinimumSectionCoverageRatio,
            left,
            right,
            coverage,
            widthA,
            widthB,
            a[0],
            b[0],
            a[^1],
            b[^1]);
    }

    /// <summary>分位數。</summary>
    /// <remarks>來自 Hank Scan3D 的 <c>Scan3D.Processing.Legacy.Stitching.StitchMath</c>。</remarks>
    /// <param name="values">數值。</param>
    /// <param name="q">分位 0～1。</param>
    internal static double Quantile(IEnumerable<double> values, double q)
    {
        var a = values.Order().ToArray();
        if (a.Length == 0)
        {
            throw new InvalidOperationException("沒有足夠的量測點。");
        }

        double t = (a.Length - 1) * q;
        int i = (int)Math.Floor(t), j = Math.Min(i + 1, a.Length - 1);
        return a[i] * (1 - (t - i)) + a[j] * (t - i);
    }

    /// <summary>中位數。</summary>
    /// <remarks>來自 Hank Scan3D 的 <c>Scan3D.Processing.Legacy.Stitching.StitchMath</c>。</remarks>
    /// <param name="values">數值。</param>
    internal static double Median(IEnumerable<double> values)
    {
        return Quantile(values, .5);
    }

    /// <summary>高斯消去求解。</summary>
    /// <remarks>來自 Hank Scan3D 的 <c>Scan3D.Processing.Legacy.Stitching.StitchMath</c>。</remarks>
    /// <param name="matrix">係數矩陣。</param>
    /// <param name="values">右側。</param>
    internal static double[] Solve(double[,] matrix, double[] values)
    {
        int n = values.Length;
        var a = new double[n, n + 1];
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++)
            {
                a[i, j] = matrix[i, j];
            }

            a[i, n] = values[i];
        }

        for (int c = 0; c < n; c++)
        {
            int best = c;
            for (int r = c + 1; r < n; r++)
            {
                if (Math.Abs(a[r, c]) > Math.Abs(a[best, c]))
                {
                    best = r;
                }
            }

            if (Math.Abs(a[best, c]) < 1e-12)
            {
                throw new InvalidOperationException("取樣幾何退化，無法求解拼接姿態。");
            }

            for (int j = 0; j <= n; j++)
            {
                (a[c, j], a[best, j]) = (a[best, j], a[c, j]);
            }

            double div = a[c, c];
            for (int j = 0; j <= n; j++)
            {
                a[c, j] /= div;
            }

            for (int r = 0; r < n; r++)
            {
                if (r != c)
                {
                    double f = a[r, c];
                    for (int j = 0; j <= n; j++)
                    {
                        a[r, j] -= f * a[c, j];
                    }
                }
            }
        }

        return Enumerable.Range(0, n).Select(i => a[i, n]).ToArray();
    }

    /// <summary>擬合 z = Ax + By + C。</summary>
    /// <remarks>來自 Hank Scan3D 的 <c>Scan3D.Processing.Legacy.Stitching.StitchMath</c>。</remarks>
    /// <param name="points">點。</param>
    /// <param name="minimumTolerance">容差下限。</param>
    internal static FittedSurface FitPlane(ScanVector[] points, double minimumTolerance)
    {
        if (points.Length < 100)
        {
            throw new InvalidOperationException("端部翼板取樣不足 100 點。");
        }

        var kept = Enumerable.Repeat(true, points.Length).ToArray();
        double a = 0, b = 0, c = 0;
        double[] residual = new double[points.Length];
        for (int pass = 0; pass < 5; pass++)
        {
            int count = 0;
            var sum = new ScanVector();
            for (int i = 0; i < points.Length; i++)
            {
                if (kept[i])
                {
                    sum += points[i];
                    count++;
                }
            }

            if (count < 100)
            {
                throw new InvalidOperationException("端部翼板有效支持不足。");
            }

            var mean = sum.Scale(1.0 / count);
            double xx = 0, xy = 0, yy = 0, xz = 0, yz = 0;
            for (int i = 0; i < points.Length; i++)
            {
                if (kept[i])
                {
                    var d = points[i] - mean;
                    xx += d.X * d.X;
                    xy += d.X * d.Y;
                    yy += d.Y * d.Y;
                    xz += d.X * d.Z;
                    yz += d.Y * d.Z;
                }
            }

            double determinant = xx * yy - xy * xy;
            if (Math.Abs(determinant) < 1e-12)
            {
                throw new InvalidOperationException("翼板點共線，無法建立平面。");
            }

            a = (xz * yy - yz * xy) / determinant;
            b = (yz * xx - xz * xy) / determinant;
            c = mean.Z - a * mean.X - b * mean.Y;
            for (int i = 0; i < points.Length; i++)
            {
                residual[i] = points[i].Z - (a * points[i].X + b * points[i].Y + c);
            }

            double center = Median(residual.Where((_, i) => kept[i]));
            double mad = Median(residual.Where((_, i) => kept[i]).Select(r => Math.Abs(r - center)));
            double tolerance = Math.Max(minimumTolerance, 3 * 1.4826 * mad);
            kept = residual.Select(r => Math.Abs(r - center) <= tolerance).ToArray();
        }

        return new FittedSurface(a, b, c, Math.Sqrt(residual.Where((_, i) => kept[i]).Select(r => r * r).Average()), kept.Count(k => k), points.Length);
    }

    /// <summary>依 X 分箱建立截面。</summary>
    /// <remarks>來自 Hank Scan3D 的 <c>Scan3D.Processing.Legacy.Stitching.StitchMath</c>。</remarks>
    /// <param name="points">點。</param>
    /// <param name="indices">索引。</param>
    /// <param name="bin">分箱寬度。</param>
    internal static ProfilePoint[] Profile(ScanVector[] points, IEnumerable<int> indices, double bin)
    {
        return indices.GroupBy(i => (long)Math.Floor(points[i].X / bin)).OrderBy(g => g.Key).Where(g => g.Count() >= 3)
            .Select(g => new ProfilePoint(Median(g.Select(i => points[i].X)), Median(g.Select(i => points[i].Z)), g.Count())).ToArray();
    }

    /// <summary>截面 2D 剛體微調。</summary>
    /// <remarks>來自 Hank Scan3D 的 <c>Scan3D.Processing.Legacy.Stitching.StitchMath</c>。</remarks>
    /// <param name="target">目標截面。</param>
    /// <param name="source">來源截面。</param>
    /// <param name="margin">邊緣留白。</param>
    internal static double[][] RefineProfile(ProfilePoint[] target, ProfilePoint[] source, double margin)
    {
        if (target.Length < 30 || source.Length < 30)
        {
            throw new InvalidOperationException("截面輪廓支持不足。");
        }

        double lo = Math.Max(target.Min(p => p.U), source.Min(p => p.U)) + margin;
        double hi = Math.Min(target.Max(p => p.U), source.Max(p => p.U)) - margin;
        var moving = source.Where(p => p.U >= lo && p.U <= hi).Select(p => (U: p.U, Z: p.Z)).ToArray();
        double[][] transform = [[1, 0, 0], [0, 1, 0], [0, 0, 1]];
        for (int pass = 0; pass < 20; pass++)
        {
            var rows = new List<(double[] J, double Residual)>();
            foreach (var p in moving)
            {
                double best = double.PositiveInfinity, nu = 0, nz = 0, residual = 0;
                for (int j = 1; j < target.Length; j++)
                {
                    var start = target[j - 1];
                    var end = target[j];
                    double du = end.U - start.U, dz = end.Z - start.Z, length = du * du + dz * dz;
                    if (du > 1.5 || length < 1e-8)
                    {
                        continue;
                    }

                    double t = Math.Clamp(((p.U - start.U) * du + (p.Z - start.Z) * dz) / length, 0, 1);
                    double eu = p.U - (start.U + t * du), ez = p.Z - (start.Z + t * dz), distance = eu * eu + ez * ez;
                    if (distance < best)
                    {
                        best = distance;
                        nu = -dz / Math.Sqrt(length);
                        nz = du / Math.Sqrt(length);
                        residual = eu * nu + ez * nz;
                    }
                }

                if (double.IsFinite(best))
                {
                    rows.Add(([-nu * p.Z + nz * p.U, nu, nz], residual));
                }
            }

            double cutoff = Math.Max(.3, Quantile(rows.Select(r => Math.Abs(r.Residual)), .9));
            var normal = new double[3, 3];
            var rhs = new double[3];
            foreach (var row in rows.Where(r => Math.Abs(r.Residual) < cutoff))
            {
                for (int i = 0; i < 3; i++)
                {
                    rhs[i] -= row.J[i] * row.Residual;
                    for (int j = 0; j < 3; j++)
                    {
                        normal[i, j] += row.J[i] * row.J[j];
                    }
                }
            }

            var step = Solve(normal, rhs);
            if (Math.Abs(step[0]) > .1 || Math.Abs(step[1]) > 5 || Math.Abs(step[2]) > 5)
            {
                throw new InvalidOperationException("截面差異過大，拒絕強行拼接。");
            }

            double c = Math.Cos(step[0]), s = Math.Sin(step[0]);
            double[][] update = [[c, -s, step[1]], [s, c, step[2]], [0, 0, 1]];
            moving = moving.Select(p => (U: c * p.U - s * p.Z + step[1], Z: s * p.U + c * p.Z + step[2])).ToArray();
            transform = Multiply(update, transform);
            if (Math.Sqrt(step.Sum(v => v * v)) < 1e-7)
            {
                break;
            }
        }

        return transform;
    }

    private static double[][] Multiply(double[][] a, double[][] b)
    {
        return Enumerable.Range(0, 3)
            .Select(i => Enumerable.Range(0, 3).Select(j => Enumerable.Range(0, 3).Sum(k => a[i][k] * b[k][j])).ToArray()).ToArray();
    }

    /// <summary>線性插值截面高度。</summary>
    /// <remarks>來自 Hank Scan3D 的 <c>Scan3D.Processing.Legacy.Stitching.StitchMath</c>。</remarks>
    /// <param name="profile">截面。</param>
    /// <param name="u">橫向 U。</param>
    internal static double? Interpolate(ProfilePoint[] profile, double u)
    {
        int lo = 0, hi = profile.Length;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (profile[mid].U < u)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid;
            }
        }

        if (lo == 0 || lo >= profile.Length)
        {
            return null;
        }

        var a = profile[lo - 1];
        var b = profile[lo];
        if (b.U - a.U > 1.5)
        {
            return null;
        }

        return a.Z + (b.Z - a.Z) * (u - a.U) / (b.U - a.U);
    }
}
