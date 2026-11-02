using ThreeDScan.Domain.Models;
using ThreeDScan.Infrastructure.Export.Models;

namespace ThreeDScan.Infrastructure.Export;

/// <summary>兩翼平面、端部特徵與厚度擬合。</summary>
/// <remarks>由 Hank Scan3D 的 GeometryFitting 搬入，邏輯不改。</remarks>
internal static class GeometryFitting
{
    /// <summary>分位數。</summary>
    /// <remarks>來自 Hank Scan3D 的 <c>Scan3D.Processing.GeometryFitting</c>。</remarks>
    /// <param name="values">數值。</param>
    /// <param name="q">分位 0～1。</param>
    internal static double Quantile(IEnumerable<double> values, double q)
    {
        var a = values.Order().ToArray();
        if (a.Length == 0)
        {
            throw new InvalidOperationException("沒有足夠量測點。");
        }

        double t = (a.Length - 1) * q;
        int i = (int)t, j = Math.Min(i + 1, a.Length - 1);
        return a[i] + (a[j] - a[i]) * (t - i);
    }

    /// <summary>穩健平面擬合。</summary>
    /// <remarks>來自 Hank Scan3D 的 <c>Scan3D.Processing.GeometryFitting</c>。</remarks>
    /// <param name="points">點。</param>
    /// <param name="selected">選用索引；null 用全部。</param>
    /// <param name="minimumPoints">最少點數。</param>
    internal static PlaneFit Plane(ScanVector[] points, int[]? selected = null, int minimumPoints = 100)
    {
        int[] indices = selected ?? Enumerable.Range(0, points.Length).ToArray();
        if (indices.Length < minimumPoints)
        {
            throw new InvalidOperationException($"板面取樣不足 {minimumPoints} 點。");
        }

        int[] original = indices;
        ScanVector normal = new ScanVector();
        double d = 0;
        for (int pass = 0; pass < 6; pass++)
        {
            if (indices.Length < minimumPoints)
            {
                throw new InvalidOperationException("板面有效取樣不足。");
            }

            var mean = new ScanVector(
                indices.Average(i => points[i].X),
                indices.Average(i => points[i].Y),
                indices.Average(i => points[i].Z));
            var covariance = new double[3, 3];
            foreach (int index in indices)
            {
                var p = points[index] - mean;
                double[] v = [p.X, p.Y, p.Z];
                for (int i = 0; i < 3; i++)
                {
                    for (int j = 0; j < 3; j++)
                    {
                        covariance[i, j] += v[i] * v[j];
                    }
                }
            }

            var (smallest, second) = SmallestEigenvector(covariance);
            if (second < 1e-8)
            {
                throw new InvalidOperationException("板面取樣共線，無法求法線。");
            }

            normal = smallest.Normalize();
            if (normal.Z < 0)
            {
                normal = normal.Scale(-1);
            }

            d = normal.Dot(mean);
            double median = Quantile(indices.Select(i => normal.Dot(points[i]) - d), .5);
            double mad = Quantile(indices.Select(i => Math.Abs(normal.Dot(points[i]) - d - median)), .5);
            double tolerance = Math.Max(.12, 3 * 1.4826 * mad);
            indices = original.Where(i => Math.Abs(normal.Dot(points[i]) - d - median) <= tolerance).ToArray();
        }

        if (indices.Length < minimumPoints)
        {
            throw new InvalidOperationException("板面有效取樣不足。");
        }

        double rms = Math.Sqrt(indices.Average(i => Math.Pow(normal.Dot(points[i]) - d, 2)));
        return new PlaneFit
        {
            Normal = normal,
            D = d,
            Rms = rms,
            Indices = indices,
        };
    }

    /// <summary>擬合兩翼。</summary>
    /// <remarks>來自 Hank Scan3D 的 <c>Scan3D.Processing.GeometryFitting</c>。</remarks>
    /// <param name="points">掃描器座標點。</param>
    /// <param name="rawIndices">原始 PLY 索引。</param>
    /// <param name="maximumRms">RMS 上限（mm）。</param>
    internal static WingFit FitWings(ScanVector[] points, int[] rawIndices, double maximumRms)
    {
        if (points.Length < 500)
        {
            throw new InvalidOperationException("兩翼幾何至少需要 500 個量測點。");
        }

        var peaks = points.GroupBy(p => (long)Math.Floor(p.Y / .5)).Where(g => g.Count() >= 10)
            .Select(g => g.MaxBy(p => p.Z)).ToArray();
        if (peaks.Length < 5)
        {
            throw new InvalidOperationException("掃描列不足，無法辨識兩翼與長軸。");
        }

        double my = peaks.Average(p => p.Y), mx = Quantile(peaks.Select(p => p.X), .5);
        double slope = 0;
        for (int pass = 0; pass < 4; pass++)
        {
            var filtered = peaks.Where(p => Math.Abs(p.X - (mx + slope * (p.Y - my))) < Math.Max(8,
                Quantile(peaks.Select(q => Math.Abs(q.X - (mx + slope * (q.Y - my)))), .5) * 4)).ToArray();
            if (filtered.Length < 5)
            {
                break;
            }

            my = filtered.Average(p => p.Y);
            mx = filtered.Average(p => p.X);
            double yy = filtered.Sum(p => (p.Y - my) * (p.Y - my));
            slope = yy > 1e-8 ? filtered.Sum(p => (p.Y - my) * (p.X - mx)) / yy : 0;
        }

        var leftIndices = Enumerable.Range(0, points.Length).Where(i => points[i].X < mx + slope * (points[i].Y - my) - 3).ToArray();
        var rightIndices = Enumerable.Range(0, points.Length).Where(i => points[i].X > mx + slope * (points[i].Y - my) + 3).ToArray();
        var left = Plane(points, leftIndices);
        var right = Plane(points, rightIndices);
        double angle = Math.Acos(Math.Clamp(left.Normal.Dot(right.Normal), -1, 1)) * 180 / Math.PI;
        if (angle < 30 || angle > 150)
        {
            throw new InvalidOperationException($"兩翼法線夾角 {angle:F2}° 不在 30～150°；此物件可能為放平夾具或單一平面。");
        }

        if (left.Rms > maximumRms || right.Rms > maximumRms)
        {
            throw new InvalidOperationException($"兩翼平面 RMS {left.Rms:F3}/{right.Rms:F3} mm 超過 {maximumRms:F3} mm。");
        }

        if (left.Indices.Length < leftIndices.Length * .45 || right.Indices.Length < rightIndices.Length * .45)
        {
            throw new InvalidOperationException("兩翼平面的有效支持比例不足 45%。");
        }

        var axis = left.Normal.Cross(right.Normal).Normalize();
        if (axis.Y < 0)
        {
            axis = axis.Scale(-1);
        }

        if (axis.Y < .5)
        {
            throw new InvalidOperationException("工件長軸與掃描器 Y 軸差異過大，須先設定掃描方向或 ROI。");
        }

        var up = (left.Normal + right.Normal).Normalize();
        var cross = axis.Cross(up).Normalize();
        up = cross.Cross(axis).Normalize();
        // Minimum-norm point on nL·x=dL and nR·x=dR; arbitrary along the ridge is removed below.
        double dot = left.Normal.Dot(right.Normal), denominator = 1 - dot * dot;
        var ridge = left.Normal.Scale((left.D - dot * right.D) / denominator) + right.Normal.Scale((right.D - dot * left.D) / denominator);
        return new WingFit
        {
            Left = left,
            Right = right,
            RidgeOrigin = ridge,
            LongAxis = axis,
            CrossAxis = cross,
            UpAxis = up,
            NormalAngle = angle,
            Points = points,
            RawIndices = rawIndices,
        };
    }

    /// <summary>由兩翼擬合建立單段幾何。</summary>
    /// <remarks>來自 Hank Scan3D 的 <c>Scan3D.Processing.GeometryFitting</c>。</remarks>
    /// <param name="fit">兩翼。</param>
    /// <param name="transform">掃描器→絕對。</param>
    /// <param name="innerLeft">左內側點。</param>
    /// <param name="innerRight">右內側點。</param>
    /// <param name="coordinates">座標選項。</param>
    /// <param name="options">單段選項。</param>
    internal static SurfaceGeometry Geometry(
        WingFit fit,
        double[][] transform,
        Point3? innerLeft,
        Point3? innerRight,
        CoordinateTransformOptions coordinates,
        SegmentProcessingOptions options)
    {
        var along = fit.Points.Select(p => (p - fit.RidgeOrigin).Dot(fit.LongAxis)).ToArray();
        double head = Quantile(along, .01), tail = Quantile(along, .99);
        HeadFeatures Features(double station, bool upper)
        {
            int[] indices = Enumerable.Range(0, fit.Points.Length).Where(i => upper ? along[i] >= station - 12 : along[i] <= station + 12).ToArray();
            if (indices.Length < 100)
            {
                throw new InvalidOperationException("端部可用量測點不足。");
            }

            double left = Quantile(indices.Select(i => (fit.Points[i] - fit.RidgeOrigin).Dot(fit.CrossAxis)), .01);
            double right = Quantile(indices.Select(i => (fit.Points[i] - fit.RidgeOrigin).Dot(fit.CrossAxis)), .99);
            var apex = fit.RidgeOrigin + fit.LongAxis.Scale(station);
            ScanVector Corner(double u, PlaneFit plane)
            {
                var basePoint = apex + fit.CrossAxis.Scale(u);
                double w = (plane.D - plane.Normal.Dot(basePoint)) / plane.Normal.Dot(fit.UpAxis);
                return basePoint + fit.UpAxis.Scale(w);
            }

            return new HeadFeatures
            {
                LeftCorner = RigidTransforms.ToPoint(RigidTransforms.Apply(transform, Corner(left, fit.Left))),
                Apex = RigidTransforms.ToPoint(RigidTransforms.Apply(transform, apex)),
                RightCorner = RigidTransforms.ToPoint(RigidTransforms.Apply(transform, Corner(right, fit.Right))),
            };
        }

        var hf = Features(head, false);
        var tf = Features(tail, true);
        var origin = RigidTransforms.ToVector(hf.Apex);
        var xx = RigidTransforms.Direction(transform, fit.CrossAxis);
        var yy = RigidTransforms.Direction(transform, fit.LongAxis);
        var zz = RigidTransforms.Direction(transform, fit.UpAxis);
        // Pose5 is a projection on the documented Z(C)*X(A) family. Full axes retain all three rotations.
        double c = Math.Atan2(xx.Y, xx.X), a = Math.Atan2(yy.Z, zz.Z);
        var pose = new Pose5
        {
            X = origin.X,
            Y = origin.Y,
            Z = origin.Z,
            A = a * 180 / Math.PI,
            C = c * 180 / Math.PI,
        };
        var projected = RigidTransforms.FromPose(pose);
        // Frobenius inner product equals trace(Rprojected^T*Rmeasured).
        double[,] measured = { { xx.X, yy.X, zz.X }, { xx.Y, yy.Y, zz.Y }, { xx.Z, yy.Z, zz.Z } };
        double trace = 0;
        for (int i = 0; i < 3; i++)
        {
            for (int j = 0; j < 3; j++)
            {
                trace += projected[i][j] * measured[i, j];
            }
        }

        double residual = Math.Acos(Math.Clamp((trace - 1) / 2, -1, 1)) * 180 / Math.PI;
        var frame = new WorkpieceFrame
        {
            Pose = pose,
            Origin = hf.Apex,
            XAxis = RigidTransforms.ToPoint(xx),
            YAxis = RigidTransforms.ToPoint(yy),
            ZAxis = RigidTransforms.ToPoint(zz),
            Pose5ResidualDegrees = residual,
            Convention = "Right-handed: X cross-section left→right, Y head→tail, Z outward bisector; pose degrees Rz(C)*Rx(A); full axes authoritative.",
        };
        var ld = Thickness(fit, fit.Left, innerLeft, transform, coordinates, options);
        var rd = Thickness(fit, fit.Right, innerRight, transform, coordinates, options);
        double width = (RigidTransforms.ToVector(hf.RightCorner) - RigidTransforms.ToVector(hf.LeftCorner)).Length;
        double height = Math.Abs((RigidTransforms.ToVector(hf.Apex)
            - (RigidTransforms.ToVector(hf.LeftCorner) + RigidTransforms.ToVector(hf.RightCorner)).Scale(.5)).Dot(zz));
        return new SurfaceGeometry
        {
            HeadFeatures = hf,
            TailFeatures = tf,
            SegmentZDelta = tf.Apex.Z - hf.Apex.Z,
            LeftThickness = ld.ThicknessMm,
            RightThickness = rd.ThicknessMm,
            WorkpieceFrame = frame,
            LeftPlaneRmsMm = fit.Left.Rms,
            RightPlaneRmsMm = fit.Right.Rms,
            WingNormalAngleDegrees = fit.NormalAngle,
            WidthMm = width,
            HeightMm = height,
            ObservedLengthMm = tail - head,
            FeatureMethod = "Two orthogonal least-squares wing planes; fitted ridge and 1%/99% observed end bands. Endpoints are fitted scan-extent estimates, not verified physical workpiece ends.",
            LeftThicknessDiagnostic = ld,
            RightThicknessDiagnostic = rd,
        };
    }

    private static ThicknessDiagnostic Thickness(
        WingFit wings,
        PlaneFit wing,
        Point3? inner,
        double[][] transform,
        CoordinateTransformOptions coordinates,
        SegmentProcessingOptions options)
    {
        Point3? scannerInner = null;
        ThicknessDiagnostic Missing(string reason)
        {
            return new ThicknessDiagnostic
            {
                Available = false,
                ThicknessMm = null,
                InnerPointScanner = scannerInner,
                ProjectedOuterPointScanner = null,
                NearestMeasuredOuterPointScanner = null,
                NearestMeasurementDistanceMm = null,
                PlaneRmsMm = null,
                SampleCount = 0,
                SampleSourcePointIndices = [],
                Reason = reason,
                UsesPlaneExtension = false,
            };
        }

        if (inner is null)
        {
            return Missing("未提供該側內表面量測座標。");
        }

        if (new[] { inner.X, inner.Y, inner.Z }.Any(v => !double.IsFinite(v)))
        {
            return Missing("內表面座標不是有限值。");
        }

        var b = RigidTransforms.ToVector(inner);
        if (coordinates.InnerReferenceFrame == InnerReferenceFrame.Absolute)
        {
            b = RigidTransforms.Apply(RigidTransforms.Inverse(transform), b);
        }

        scannerInner = RigidTransforms.ToPoint(b);
        // Radius follows the wing plane, so a valid 30 mm plate is not excluded by its normal depth.
        double estimated = wing.D - wing.Normal.Dot(b);
        var estimate = b + wing.Normal.Scale(estimated);
        int[] local = wing.Indices.Where(i => (wings.Points[i] - estimate).Length <= options.ThicknessLocalRadiusMm).ToArray();
        if (local.Length < 100)
        {
            return Missing($"相應翼板 {options.ThicknessLocalRadiusMm:G} mm 局部範圍內不足 100 個量測點。");
        }

        PlaneFit plane;
        try
        {
            plane = Plane(wings.Points, local);
        }
        catch (InvalidOperationException ex)
        {
            return Missing("局部板面無法擬合：" + ex.Message);
        }

        double t = plane.D - plane.Normal.Dot(b);
        var projected = b + plane.Normal.Scale(t);
        int nearest = plane.Indices.MinBy(i => (wings.Points[i] - projected).Length);
        double distance = (wings.Points[nearest] - projected).Length;
        string? reason = plane.Rms > options.MaximumPlaneRmsMm ? "局部平面 RMS 超標。" :
            t < .1 || t > 80 ? "有向板厚不在 0.1～80 mm；請確認內外表面及座標系。" :
            distance > 30 ? "投影點距最近實測板面超過 30 mm。" : null;
        return new ThicknessDiagnostic
        {
            Available = reason is null,
            ThicknessMm = reason is null ? t : null,
            InnerPointScanner = RigidTransforms.ToPoint(b),
            ProjectedOuterPointScanner = RigidTransforms.ToPoint(projected),
            NearestMeasuredOuterPointScanner = RigidTransforms.ToPoint(wings.Points[nearest]),
            NearestMeasurementDistanceMm = distance,
            PlaneRmsMm = plane.Rms,
            SampleCount = plane.Indices.Length,
            SampleSourcePointIndices = plane.Indices.Select(i => wings.RawIndices[i]).ToArray(),
            Reason = reason,
            UsesPlaneExtension = distance > 2,
        };
    }

    private static (ScanVector Vector, double SecondEigenvalue) SmallestEigenvector(double[,] m)
    {
        var a = (double[,])m.Clone();
        double[,] v = { { 1, 0, 0 }, { 0, 1, 0 }, { 0, 0, 1 } };
        for (int pass = 0; pass < 30; pass++)
        {
            int p = 0, q = 1;
            for (int i = 0; i < 3; i++)
            {
                for (int j = i + 1; j < 3; j++)
                {
                    if (Math.Abs(a[i, j]) > Math.Abs(a[p, q]))
                    {
                        p = i;
                        q = j;
                    }
                }
            }

            if (Math.Abs(a[p, q]) < 1e-10)
            {
                break;
            }

            double phi = .5 * Math.Atan2(2 * a[p, q], a[q, q] - a[p, p]), c = Math.Cos(phi), s = Math.Sin(phi);
            var rotation = new double[,] { { 1, 0, 0 }, { 0, 1, 0 }, { 0, 0, 1 } };
            rotation[p, p] = c;
            rotation[q, q] = c;
            rotation[p, q] = s;
            rotation[q, p] = -s;
            var temp = new double[3, 3];
            var next = new double[3, 3];
            var nv = new double[3, 3];
            for (int i = 0; i < 3; i++)
            {
                for (int j = 0; j < 3; j++)
                {
                    for (int k = 0; k < 3; k++)
                    {
                        temp[i, j] += a[i, k] * rotation[k, j];
                        nv[i, j] += v[i, k] * rotation[k, j];
                    }
                }
            }

            for (int i = 0; i < 3; i++)
            {
                for (int j = 0; j < 3; j++)
                {
                    for (int k = 0; k < 3; k++)
                    {
                        next[i, j] += rotation[k, i] * temp[k, j];
                    }
                }
            }

            a = next;
            v = nv;
        }

        var order = Enumerable.Range(0, 3).OrderBy(i => a[i, i]).ToArray();
        int smallest = order[0];
        return (new ScanVector(v[0, smallest], v[1, smallest], v[2, smallest]), a[order[1], order[1]]);
    }
}
