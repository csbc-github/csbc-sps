using ThreeDScan.Domain.Models;
using ThreeDScan.Infrastructure.Export.Models;

namespace ThreeDScan.Infrastructure.Export;

/// <summary>兩翼平面、端部特徵與厚度擬合。</summary>
/// <remarks>在原始掃描器歐氏座標分離有實測支持的平面，再建立兩翼幾何。</remarks>
internal static class GeometryFitting
{
    /// <summary>分位數。</summary>
    /// <remarks>保留原本線性插值；精確選取相鄰順序統計量，無須排序整份量測。</remarks>
    /// <param name="values">數值。</param>
    /// <param name="q">分位 0～1。</param>
    internal static double Quantile(IEnumerable<double> values, double q)
    {
        return QuantileOwned(values.ToArray(), q);
    }

    private static double QuantileOwned(double[] a, double q)
    {
        if (a.Length == 0)
        {
            throw new InvalidOperationException("沒有足夠量測點。");
        }

        double t = (a.Length - 1) * q;
        int i = (int)t, j = Math.Min(i + 1, a.Length - 1);
        if ((uint)i >= a.Length || (uint)j >= a.Length)
            throw new IndexOutOfRangeException();
        double lower = Select(a, i), upper = lower;
        // Selection partitions every remaining value at or above rank i.
        // The next order statistic is therefore the minimum of that suffix.
        if (j != i)
        {
            upper = a[j];
            for (int k = j + 1; k < a.Length; k++)
                if (a[k].CompareTo(upper) < 0) upper = a[k];
        }
        return lower + (upper - lower) * (t - i);
    }

    private static double Select(double[] values, int rank)
    {
        int left = 0, right = values.Length - 1;
        int budget = 2 * (System.Numerics.BitOperations.Log2((uint)values.Length) + 1);
        while (right - left > 24 && budget-- > 0)
        {
            double a = values[left], b = values[left + (right - left) / 2], c = values[right];
            if (a.CompareTo(b) > 0) (a, b) = (b, a);
            if (b.CompareTo(c) > 0) (b, c) = (c, b);
            if (a.CompareTo(b) > 0) (a, b) = (b, a);
            double pivot = b;
            int low = left, at = left, high = right;
            while (at <= high)
            {
                int order = values[at].CompareTo(pivot);
                if (order < 0)
                {
                    (values[low], values[at]) = (values[at], values[low]);
                    low++; at++;
                }
                else if (order > 0)
                {
                    (values[at], values[high]) = (values[high], values[at]);
                    high--;
                }
                else at++;
            }
            if (rank < low) right = low - 1;
            else if (rank > high) left = high + 1;
            else return values[rank];
        }
        // The bounded fallback prevents adversarial input from causing a
        // quadratic quickselect while preserving the same exact order values.
        Array.Sort(values, left, right - left + 1);
        return values[rank];
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

            double sumX = 0, sumY = 0, sumZ = 0;
            foreach (int index in indices)
            {
                var p = points[index];
                sumX += p.X; sumY += p.Y; sumZ += p.Z;
            }
            var mean = new ScanVector(sumX / indices.Length, sumY / indices.Length, sumZ / indices.Length);
            double xx = 0, xy = 0, xz = 0, yy = 0, yz = 0, zz = 0;
            foreach (int index in indices)
            {
                var p = points[index] - mean;
                xx += p.X * p.X; xy += p.X * p.Y; xz += p.X * p.Z;
                yy += p.Y * p.Y; yz += p.Y * p.Z; zz += p.Z * p.Z;
            }
            double[,] covariance = { { xx, xy, xz }, { xy, yy, yz }, { xz, yz, zz } };

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
            var residuals = new double[indices.Length];
            for (int i = 0; i < indices.Length; i++) residuals[i] = normal.Dot(points[indices[i]]) - d;
            double median = QuantileOwned(residuals, .5);
            for (int i = 0; i < residuals.Length; i++) residuals[i] = Math.Abs(residuals[i] - median);
            double mad = QuantileOwned(residuals, .5);
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
        if (TryFitWings(points, rawIndices, maximumRms, out var fit, out var reason)) return fit!;
        throw new InvalidOperationException(reason);
    }

    /// <summary>Reject normal non-angle candidates without throwing during candidate enumeration.</summary>
    internal static bool TryFitWings(ScanVector[] points, int[] rawIndices, double maximumRms,
        out WingFit? result, out string? reason, CancellationToken cancellationToken = default,
        PlaneFit[]? planeCandidates = null)
    {
        result = null;
        reason = "未找到兩個有足夠實測翼寬及共同長向範圍的不同板面；單一板面或厚度側邊不能當作角鋼兩翼。";
        if (points.Length < 500)
        {
            reason = "兩翼幾何至少需要 500 個量測點。";
            return false;
        }
        var planes = planeCandidates ?? WingPlaneSegmentation.FindPlanes(points, Math.Clamp(maximumRms * 1.5, .25, 1), 4, cancellationToken);
        double bestScore = 0;
        int rejectionStage = 0;
        double rejectionSupport = -1;
        string? rejectionReason = reason;
        for (int i = 0; i < planes.Length; i++)
        for (int j = i + 1; j < planes.Length; j++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var left = planes[i];
            var right = planes[j];
            double dot = Math.Clamp(left.Normal.Dot(right.Normal), -1, 1);
            double angle = Math.Acos(dot) * 180 / Math.PI;
            double support = (left.Indices.Length + right.Indices.Length) / (double)points.Length;
            if (angle < 30 || angle > 150)
            {
                Reject(1, $"板面法線夾角 {angle:F2}° 不在 30～150°。", support);
                continue;
            }
            if (left.Rms > maximumRms || right.Rms > maximumRms)
            {
                Reject(2, $"兩板面 RMS {left.Rms:F3}/{right.Rms:F3} mm 超過 {maximumRms:F3} mm。", support);
                continue;
            }
            if (support < .7)
            {
                Reject(3, $"兩板面合計支持 {support:P1}，不足物件量測的 70%。", support);
                continue;
            }

            var axis = left.Normal.Cross(right.Normal).Normalize();
            if (axis.Y < -1e-6 || Math.Abs(axis.Y) <= 1e-6 && axis.X < 0) axis = axis.Scale(-1);
            double denominator = 1 - dot * dot;
            var ridge = left.Normal.Scale((left.D - dot * right.D) / denominator)
                + right.Normal.Scale((right.D - dot * left.D) / denominator);
            var up = (left.Normal + right.Normal).Normalize();
            var cross = axis.Cross(up).Normalize();
            up = cross.Cross(axis).Normalize();
            var leftAlong = Extent(left, axis);
            var rightAlong = Extent(right, axis);
            double overlap = Math.Min(leftAlong.High, rightAlong.High) - Math.Max(leftAlong.Low, rightAlong.Low);
            double length = Math.Max(leftAlong.High - leftAlong.Low, rightAlong.High - rightAlong.Low);
            if (overlap < 15 || overlap < length * .5)
            {
                Reject(4, $"兩板面共同長向範圍 {overlap:F2} mm，不足 15 mm 或較長板面的 50%。", support);
                continue;
            }
            var leftAcross = Extent(left, left.Normal.Cross(axis).Normalize());
            var rightAcross = Extent(right, right.Normal.Cross(axis).Normalize());
            double leftWidth = leftAcross.High - leftAcross.Low, rightWidth = rightAcross.High - rightAcross.Low;
            // A dense, thin board edge can otherwise pass an angle and point-count check.
            if (Math.Min(leftWidth, rightWidth) < Math.Max(8, Math.Max(leftWidth, rightWidth) * .08))
            {
                Reject(5, $"兩板面實測翼寬 {leftWidth:F2}/{rightWidth:F2} mm，不足最小翼寬；厚度側邊不能當作翼板。", support);
                continue;
            }
            if (!NearRidge(left) || !NearRidge(right))
            {
                Reject(6, "板面交線附近缺少沿共同長向連續分布的 5 mm 內實測外緣，或交線穿過板面內部。", support);
                continue;
            }
            if (Extent(left, cross).Center > Extent(right, cross).Center) (left, right) = (right, left);
            double score = Math.Min(leftWidth, rightWidth) * overlap
                // A dense broad face must not make a sparsely measured end face
                // outrank the independently supported second wing.
                * Math.Min(1, 2 * Math.Sqrt((double)left.Indices.Length * right.Indices.Length) / points.Length)
                / (1 + left.Rms + right.Rms);
            if (score <= bestScore) continue;
            bestScore = score;
            result = new WingFit
            {
                Left = left, Right = right, RidgeOrigin = ridge, LongAxis = axis,
                CrossAxis = cross, UpAxis = up, NormalAngle = angle, Points = points, RawIndices = rawIndices,
            };

            bool NearRidge(PlaneFit plane)
            {
                var across = plane.Normal.Cross(axis).Normalize();
                var extent = Extent(plane, across);
                double station = ridge.Dot(across);
                // The common ridge bounds each wing. A supporting plate may
                // cross the same intersection well inside its measured area;
                // that is not evidence of a second angle-steel wing.
                if (station >= extent.Low - 5 && station <= extent.High + 5
                    && Math.Min(Math.Abs(station - extent.Low), Math.Abs(station - extent.High)) <= 5)
                    return true;
                if (station > extent.Low + 5 && station < extent.High - 5) return false;

                // Population quantiles trim a wider physical strip on a broad
                // wing. Inspect original measurements before rejecting its rim;
                // isolated outliers or a short end patch cannot supply it.
                double low = Math.Max(leftAlong.Low, rightAlong.Low);
                double high = Math.Min(leftAlong.High, rightAlong.High);
                int minimum = Math.Max(100, (int)Math.Ceiling(plane.Indices.Length * .001));
                int perBin = Math.Max(3, (int)Math.Ceiling(minimum / 20.0));
                var bins = new int[10];
                int count = 0, at = 0;
                foreach (int index in plane.Indices)
                {
                    if ((at++ & 16383) == 0) cancellationToken.ThrowIfCancellationRequested();
                    var point = points[index];
                    if (Math.Abs(point.Dot(across) - station) > 5) continue;
                    double along = point.Dot(axis);
                    if (along < low || along > high) continue;
                    bins[Math.Min(9, (int)((along - low) / (high - low) * 10))]++;
                    count++;
                }
                return count >= minimum && bins.Count(n => n >= perBin) >= 6;
            }
        }
        if (result is null)
        {
            reason = rejectionReason;
            return false;
        }
        reason = null;
        return true;

        void Reject(int stage, string detail, double support)
        {
            if (stage < rejectionStage || stage == rejectionStage && support <= rejectionSupport) return;
            rejectionStage = stage;
            rejectionSupport = support;
            rejectionReason = detail;
        }

        (double Low, double High, double Center) Extent(PlaneFit plane, ScanVector direction)
        {
            int stride = Math.Max(1, plane.Indices.Length / 8192);
            var values = plane.Indices.Where((_, index) => index % stride == 0)
                .Select(index => points[index].Dot(direction)).Order().ToArray();
            double At(double q) => values[(int)((values.Length - 1) * q)];
            return (At(.01), At(.99), At(.5));
        }
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
        var supported = fit.Left.Indices.Concat(fit.Right.Indices).Distinct().ToArray();
        double head = Quantile(supported.Select(i => along[i]), .01), tail = Quantile(supported.Select(i => along[i]), .99);
        HeadFeatures Features(double station, bool upper)
        {
            int[] leftIndices = fit.Left.Indices.Where(i => upper ? along[i] >= station - 12 : along[i] <= station + 12).ToArray();
            int[] rightIndices = fit.Right.Indices.Where(i => upper ? along[i] >= station - 12 : along[i] <= station + 12).ToArray();
            if (leftIndices.Length < 100 || rightIndices.Length < 100)
            {
                throw new InvalidOperationException("端部各翼板的有效實測支持不足 100 點。");
            }

            double left = Quantile(leftIndices.Select(i => (fit.Points[i] - fit.RidgeOrigin).Dot(fit.CrossAxis)), .01);
            double right = Quantile(rightIndices.Select(i => (fit.Points[i] - fit.RidgeOrigin).Dot(fit.CrossAxis)), .99);
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
            Convention = "Right-handed: X cross-section left→right, Y head→tail oriented toward scanner +Y (+X when Y is zero), Z outward bisector; not an inferred machine travel direction. Pose degrees Rz(C)*Rx(A); full axes authoritative.",
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
            FeatureMethod = "Two independently supported robust wing planes; fitted ridge and per-wing 1%/99% measured end bands. Endpoints are fitted scan-extent estimates, not verified physical workpiece ends.",
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
