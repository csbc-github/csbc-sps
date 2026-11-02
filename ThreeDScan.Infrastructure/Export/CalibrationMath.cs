using ThreeDScan.Infrastructure.Export.Models;

namespace ThreeDScan.Infrastructure.Export;

/// <summary>邊緣直線與四角地面平面擬合。</summary>
/// <remarks>由 Hank Scan3D 的 CalibrationMath 搬入，邏輯不改。</remarks>
internal static class CalibrationMath
{
    /// <summary>直線（中心＋方向）。</summary>
    internal readonly struct Line
    {
        /// <summary>建立直線。</summary>
        /// <param name="center">中心。</param>
        /// <param name="direction">方向。</param>
        public Line(ScanVector center, ScanVector direction)
        {
            Center = center;
            Direction = direction;
        }

        /// <summary>中心。</summary>
        public ScanVector Center { get; }

        /// <summary>方向。</summary>
        public ScanVector Direction { get; }
    }

    /// <summary>中位數。</summary>
    /// <remarks>來自 Hank Scan3D 的 <c>Scan3D.Processing.Legacy.Extraction.CalibrationMath</c>。</remarks>
    /// <param name="values">數值。</param>
    internal static double Median(IEnumerable<double> values)
    {
        var a = values.Order().ToArray();
        int m = a.Length / 2;
        return a.Length % 2 == 1 ? a[m] : (a[m - 1] + a[m]) / 2.0;
    }

    /// <summary>最小二乘直線。</summary>
    /// <remarks>來自 Hank Scan3D 的 <c>Scan3D.Processing.Legacy.Extraction.CalibrationMath</c>。</remarks>
    /// <param name="points">點。</param>
    internal static Line FitLine(ScanVector[] points)
    {
        var sum = new ScanVector();
        foreach (var p in points)
        {
            sum += p;
        }

        var c = sum.Scale(1.0 / points.Length);
        double xx = 0, xy = 0, xz = 0, yy = 0, yz = 0, zz = 0;
        foreach (var p in points)
        {
            double dx = p.X - c.X, dy = p.Y - c.Y, dz = p.Z - c.Z;
            xx += dx * dx;
            xy += dx * dy;
            xz += dx * dz;
            yy += dy * dy;
            yz += dy * dz;
            zz += dz * dz;
        }

        // Preserve the original covariance-row line estimator, including stable tie selection.
        double[] ranges =
        [
            Math.Max(Math.Max(Math.Abs(xx), Math.Abs(xy)), Math.Abs(xz)),
            Math.Max(Math.Max(Math.Abs(xy), Math.Abs(yy)), Math.Abs(yz)),
            Math.Max(Math.Max(Math.Abs(xz), Math.Abs(yz)), Math.Abs(zz)),
        ];
        ScanVector[] dirs = [new ScanVector(xx, xy, xz), new ScanVector(xy, yy, yz), new ScanVector(xz, yz, zz)];
        return new Line(c, dirs[Array.IndexOf(ranges, ranges.Max())].Normalize());
    }

    /// <summary>穩健直線（20 次重排）。</summary>
    /// <remarks>來自 Hank Scan3D 的 <c>Scan3D.Processing.Legacy.Extraction.CalibrationMath</c>。</remarks>
    /// <param name="points">點。</param>
    internal static Line RobustLine(ScanVector[] points)
    {
        // Original keep_pct=100 still reorders points on each of 20 fitting passes.
        var kept = points;
        var line = new Line();
        for (int pass = 0; pass < 20; pass++)
        {
            line = FitLine(kept);
            var current = line;
            kept = kept.OrderBy(p => (p - current.Center).Cross(current.Direction).Length).ToArray();
        }

        return line;
    }

    /// <summary>兩直線最近中點。</summary>
    /// <remarks>來自 Hank Scan3D 的 <c>Scan3D.Processing.Legacy.Extraction.CalibrationMath</c>。</remarks>
    /// <param name="a">直線 A。</param>
    /// <param name="b">直線 B。</param>
    internal static ScanVector ClosestOrigin(Line a, Line b)
    {
        var p13 = a.Center - b.Center;
        double d1343 = p13.Dot(b.Direction), d4321 = b.Direction.Dot(a.Direction), d1321 = p13.Dot(a.Direction);
        double d4343 = b.Direction.Dot(b.Direction), d2121 = a.Direction.Dot(a.Direction);
        double denominator = d2121 * d4343 - d4321 * d4321;
        if (Math.Abs(denominator) < 1e-9)
        {
            return a.Center.Scale(.5) + b.Center.Scale(.5);
        }

        double mua = (d1343 * d4321 - d1321 * d4343) / denominator;
        double mub = (d1343 + mua * d4321) / d4343;
        return (a.Center + a.Direction.Scale(mua)).Scale(.5) + (b.Center + b.Direction.Scale(mub)).Scale(.5);
    }

    /// <summary>擬合 z = Ax + By + C。</summary>
    /// <remarks>來自 Hank Scan3D 的 <c>Scan3D.Processing.Legacy.Extraction.CalibrationMath</c>。</remarks>
    /// <param name="points">點。</param>
    internal static (double A, double B, double C) FitPlane(List<ScanVector> points)
    {
        double sx = 0, sy = 0, sz = 0, sxx = 0, syy = 0, sxy = 0, sxz = 0, syz = 0;
        foreach (var p in points)
        {
            sx += p.X;
            sy += p.Y;
            sz += p.Z;
            sxx += p.X * p.X;
            syy += p.Y * p.Y;
            sxy += p.X * p.Y;
            sxz += p.X * p.Z;
            syz += p.Y * p.Z;
        }

        double[][] rows = [[sxx, sxy, sx, sxz], [sxy, syy, sy, syz], [sx, sy, points.Count, sz]];
        for (int c = 0; c < 3; c++)
        {
            int best = c;
            for (int r = c + 1; r < 3; r++)
            {
                if (Math.Abs(rows[r][c]) > Math.Abs(rows[best][c]))
                {
                    best = r;
                }
            }

            (rows[c], rows[best]) = (rows[best], rows[c]);
            double div = rows[c][c];
            if (Math.Abs(div) < 1e-12)
            {
                throw new CalibrationMathException("Degenerate corner points");
            }

            for (int cc = 0; cc < 4; cc++)
            {
                rows[c][cc] /= div;
            }

            for (int r = 0; r < 3; r++)
            {
                if (r == c)
                {
                    continue;
                }

                double f = rows[r][c];
                for (int cc = 0; cc < 4; cc++)
                {
                    rows[r][cc] -= f * rows[c][cc];
                }
            }
        }

        return (rows[0][3], rows[1][3], rows[2][3]);
    }
}
