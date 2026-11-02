using ThreeDScan.Infrastructure.Export.Models;

namespace ThreeDScan.Infrastructure.Export;

/// <summary>
/// 移植 ply_processor/_internal/hole-detector.js。須保留 XYZ 順序與掃描器座標。
/// 計算用 double；Float32 輸入升級時不改動量測座標。
/// </summary>
/// <remarks>由 Hank Scan3D 的 OrganizedHoleDetector 搬入，邏輯不改。</remarks>
public static class OrganizedHoleDetector
{
    private static double Dot(double[] a, double[] b)
    {
        return a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
    }

    private static double[] Cross(double[] a, double[] b)
    {
        return [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]];
    }

    private static double Norm(double[] a)
    {
        return Math.Sqrt(Dot(a, a));
    }

    private static double[] Normalized(double[] a)
    {
        var n = Norm(a);
        return a.Select(x => x / n).ToArray();
    }

    private static double Median(IEnumerable<double> values)
    {
        var a = values.Order().ToArray();
        int m = a.Length / 2;
        return a.Length == 0 ? 0 : a.Length % 2 != 0 ? a[m] : (a[m - 1] + a[m]) / 2;
    }

    // JavaScript Math.round ties go toward +infinity, including for negative coordinates.
    private static double Key(double value)
    {
        var scaled = value * 10000;
        var floor = Math.Floor(scaled);
        return floor + (scaled - floor >= .5 ? 1 : 0);
    }

    private static double Pitch(IEnumerable<double> values)
    {
        var sorted = values.Select(Key).Distinct().Order().ToArray();
        return Median(sorted.Skip(1).Select((v, i) => (v - sorted[i]) / 10000).Where(v => v > 0));
    }

    private static double Distance(HoleFace plane, double[] positions, int i)
    {
        return plane.Normal[0] * positions[i * 3] + plane.Normal[1] * positions[i * 3 + 1]
            + plane.Normal[2] * positions[i * 3 + 2] + plane.D;
    }

    private static HoleFace FitPlane(double[] positions, int[] indices)
    {
        var origin = new double[3];
        foreach (int i in indices)
        {
            for (int j = 0; j < 3; j++)
            {
                origin[j] += positions[i * 3 + j] / indices.Length;
            }
        }

        var a = new double[3, 3];
        double[,] v = { { 1, 0, 0 }, { 0, 1, 0 }, { 0, 0, 1 } };
        foreach (int i in indices)
        {
            var d = Enumerable.Range(0, 3).Select(j => positions[i * 3 + j] - origin[j]).ToArray();
            for (int j = 0; j < 3; j++)
            {
                for (int k = 0; k < 3; k++)
                {
                    a[j, k] += d[j] * d[k] / indices.Length;
                }
            }
        }

        for (int iteration = 0; iteration < 32; iteration++)
        {
            int p = 0, q = 1;
            foreach (var (j, k) in new[] { (0, 2), (1, 2) })
            {
                if (Math.Abs(a[j, k]) > Math.Abs(a[p, q]))
                {
                    p = j;
                    q = k;
                }
            }

            if (Math.Abs(a[p, q]) < 1e-10)
            {
                break;
            }

            var angle = .5 * Math.Atan2(2 * a[p, q], a[q, q] - a[p, p]);
            var c = Math.Cos(angle);
            var s = Math.Sin(angle);
            var app = a[p, p];
            var aqq = a[q, q];
            var apq = a[p, q];
            a[p, p] = c * c * app - 2 * s * c * apq + s * s * aqq;
            a[q, q] = s * s * app + 2 * s * c * apq + c * c * aqq;
            a[p, q] = a[q, p] = 0;
            for (int k = 0; k < 3; k++)
            {
                if (k != p && k != q)
                {
                    var akp = a[k, p];
                    var akq = a[k, q];
                    a[k, p] = a[p, k] = c * akp - s * akq;
                    a[k, q] = a[q, k] = s * akp + c * akq;
                }

                var vkp = v[k, p];
                var vkq = v[k, q];
                v[k, p] = c * vkp - s * vkq;
                v[k, q] = s * vkp + c * vkq;
            }
        }

        int smallest = 0;
        for (int j = 1; j < 3; j++)
        {
            if (a[j, j] < a[smallest, smallest])
            {
                smallest = j;
            }
        }

        var normal = Normalized(Enumerable.Range(0, 3).Select(j => v[j, smallest]).ToArray());
        if (normal[2] < 0)
        {
            normal = normal.Select(x => -x).ToArray();
        }

        double[] reference = Math.Abs(normal[0]) < .95 ? [1, 0, 0] : [0, 1, 0];
        var projection = Dot(reference, normal);
        var uAxis = Normalized(reference.Select((x, j) => x - projection * normal[j]).ToArray());
        return new HoleFace
        {
            Origin = origin,
            Normal = normal,
            UAxis = uAxis,
            VAxis = Normalized(Cross(normal, uAxis)),
            D = -Dot(normal, origin),
        };
    }

    private static HoleFace[] FindFaces(double[] positions, int count, HoleDetectionOptions options)
    {
        uint seed = 0x6d2b79f5;
        double Random()
        {
            seed = unchecked(seed * 1664525u + 1013904223u);
            return seed / 4294967296.0;
        }

        int sampleSize = Math.Min(count, 4096);
        var sample = Enumerable.Range(0, sampleSize).Select(i => (int)Math.Floor((double)i * count / sampleSize)).ToArray();
        var available = Enumerable.Range(0, count).ToArray();
        var faces = new List<HoleFace>();
        for (int pass = 0; pass < options.MaxFaces && sample.Length >= 64; pass++)
        {
            HoleFace? best = null;
            int bestCount = 0;
            for (int trial = 0; trial < 400; trial++)
            {
                var ids = Enumerable.Range(0, 3).Select(_ => sample[(int)Math.Floor(Random() * sample.Length)]).ToArray();
                var abc = ids.Select(i => new[] { positions[i * 3], positions[i * 3 + 1], positions[i * 3 + 2] }).ToArray();
                var n = Cross(abc[1].Select((x, j) => x - abc[0][j]).ToArray(), abc[2].Select((x, j) => x - abc[0][j]).ToArray());
                if (Norm(n) < 1e-6)
                {
                    continue;
                }

                var normal = Normalized(n);
                var d = -Dot(normal, abc[0]);
                int hits = 0;
                foreach (int i in sample)
                {
                    if (Math.Abs(normal[0] * positions[i * 3] + normal[1] * positions[i * 3 + 1] + normal[2] * positions[i * 3 + 2] + d) <= options.FitToleranceMm)
                    {
                        hits++;
                    }
                }

                if (hits > bestCount)
                {
                    bestCount = hits;
                    best = new HoleFace { Normal = normal, D = d };
                }
            }

            if (best is null || bestCount < Math.Max(64, sampleSize * .025))
            {
                break;
            }

            int[] inliers = [];
            for (int refine = 0; refine < 3; refine++)
            {
                inliers = available.Where(i => Math.Abs(Distance(best, positions, i)) <= options.FitToleranceMm * 1.5).ToArray();
                if (inliers.Length < 100)
                {
                    break;
                }

                best = FitPlane(positions, inliers);
            }

            if (best.Origin.Length == 0 || inliers.Length < 100)
            {
                break;
            }

            double sum = 0;
            foreach (int i in inliers)
            {
                var residual = Math.Abs(Distance(best, positions, i));
                sum += residual * residual;
            }

            best.Id = $"F{faces.Count + 1:00}";
            best.FitPointCount = inliers.Length;
            best.RmseMm = Math.Sqrt(sum / inliers.Length);
            faces.Add(best);
            sample = sample.Where(i => Math.Abs(Distance(best, positions, i)) > options.SurfaceToleranceMm).ToArray();
            available = available.Where(i => Math.Abs(Distance(best, positions, i)) > options.SurfaceToleranceMm).ToArray();
        }

        return faces.ToArray();
    }

    private static (double U, double V, double H) Project(HoleFace plane, double x, double y, double z)
    {
        double[] d = [x - plane.Origin[0], y - plane.Origin[1], z - plane.Origin[2]];
        return (Dot(d, plane.UAxis), Dot(d, plane.VAxis), Dot(d, plane.Normal));
    }

    private static BoundaryPoint Point(double[] positions, HoleFace plane, int index)
    {
        var x = positions[index * 3];
        var y = positions[index * 3 + 1];
        var z = positions[index * 3 + 2];
        var p = Project(plane, x, y, z);
        return new BoundaryPoint(index, x, y, z, p.U, p.V, p.H);
    }

    private static BoundaryBox Box(IEnumerable<(double U, double V)> points)
    {
        double minU = double.PositiveInfinity, maxU = double.NegativeInfinity;
        double minV = double.PositiveInfinity, maxV = double.NegativeInfinity;
        foreach (var p in points)
        {
            minU = Math.Min(minU, p.U);
            maxU = Math.Max(maxU, p.U);
            minV = Math.Min(minV, p.V);
            maxV = Math.Max(maxV, p.V);
        }

        return new BoundaryBox(minU, maxU, minV, maxV, maxU - minU, maxV - minV);
    }

    private sealed class GapEvent
    {
        public GapEvent(double key, double y, double a, double b, int left, int right)
        {
            Key = key;
            Y = y;
            A = a;
            B = b;
            Left = left;
            Right = right;
        }

        public double Key { get; }

        public double Y { get; }

        public double A { get; }

        public double B { get; }

        public int Left { get; }

        public int Right { get; }
    }

    private static (List<DetectedHole> Holes, List<DetectedHole> PartialHoles, HoleDiagnostics Diagnostics) ScanFace(
        double[] positions,
        HoleFace plane,
        Dictionary<double, List<int>> rows,
        Dictionary<double, List<int>> rawRows,
        double pitchX,
        double pitchY,
        HoleDetectionOptions options,
        int assignedCount)
    {
        double X(int i)
        {
            return positions[i * 3];
        }

        double gapMinimum = Math.Max(options.MinimumOpeningMm, pitchX * 6), supportMm = Math.Max(1, pitchX * 4);
        var events = new List<GapEvent>();
        int rejectedSurfaceCutoffs = 0;
        foreach (var (key, unsorted) in rows.OrderBy(kv => kv.Key))
        {
            // OrderBy is stable, matching JavaScript's stable Array.sort even for duplicate XYZ.
            var indices = unsorted.OrderBy(X).ToArray();
            rows[key] = indices.ToList();
            for (int j = 1; j < indices.Length; j++)
            {
                int left = indices[j - 1], right = indices[j];
                double a = X(left), b = X(right);
                if (b - a < gapMinimum)
                {
                    continue;
                }

                int li = j - 1, ri = j;
                while (li > 0 && X(indices[li]) - X(indices[li - 1]) <= pitchX * 2.5 && a - X(indices[li]) < supportMm)
                {
                    li--;
                }

                while (ri + 1 < indices.Length && X(indices[ri + 1]) - X(indices[ri]) <= pitchX * 2.5 && X(indices[ri]) - b < supportMm)
                {
                    ri++;
                }

                if (a - X(indices[li]) < supportMm * .8 || X(indices[ri]) - b < supportMm * .8)
                {
                    continue;
                }

                var raw = rawRows[key];
                var margin = Math.Min(1, (b - a) * .15);
                int low = 0, high = raw.Count;
                while (low < high)
                {
                    int mid = (low + high) >> 1;
                    if (X(raw[mid]) < a + margin)
                    {
                        low = mid + 1;
                    }
                    else
                    {
                        high = mid;
                    }
                }

                int surfaceReturns = 0;
                for (int k = low; k < raw.Count && X(raw[k]) <= b - margin; k++)
                {
                    if (Math.Abs(Distance(plane, positions, raw[k])) <= options.OcclusionToleranceMm)
                    {
                        surfaceReturns++;
                    }
                }

                if (surfaceReturns > Math.Max(2, (b - a - 2 * margin) / pitchX * .25))
                {
                    rejectedSurfaceCutoffs++;
                    continue;
                }

                events.Add(new GapEvent(key, key / 10000, a, b, left, right));
            }
        }

        var parents = Enumerable.Range(0, events.Count).ToArray();
        int Find(int i)
        {
            while (parents[i] != i)
            {
                parents[i] = parents[parents[i]];
                i = parents[i];
            }

            return i;
        }

        for (int i = 0; i < events.Count; i++)
        {
            for (int j = i - 1; j >= 0; j--)
            {
                var a = events[i];
                var b = events[j];
                var dy = a.Y - b.Y;
                if (dy > pitchY * 2.1)
                {
                    break;
                }

                if (dy > pitchY * .5 && Math.Min(a.B, b.B) - Math.Max(a.A, b.A) > pitchX)
                {
                    parents[Find(i)] = Find(j);
                }
            }
        }

        var groups = new List<List<GapEvent>>();
        var groupIndex = new Dictionary<int, int>();
        for (int i = 0; i < events.Count; i++)
        {
            int key = Find(i);
            if (!groupIndex.TryGetValue(key, out int g))
            {
                g = groups.Count;
                groupIndex.Add(key, g);
                groups.Add([]);
            }

            groups[g].Add(events[i]);
        }

        int[]? CapFor(GapEvent edge, int side)
        {
            for (int offset = 1; offset <= 2; offset++)
            {
                if (!rows.TryGetValue(Key(edge.Y + side * offset * pitchY), out var indices) || indices.Count == 0)
                {
                    continue;
                }

                var margin = pitchX * 2;
                var cap = indices.Where(i => X(i) >= edge.A - margin && X(i) <= edge.B + margin).ToArray();
                if (cap.Length < 3 || X(cap[0]) > edge.A + margin || X(cap[^1]) < edge.B - margin)
                {
                    continue;
                }

                double maxGap = 0;
                for (int j = 1; j < cap.Length; j++)
                {
                    maxGap = Math.Max(maxGap, X(cap[j]) - X(cap[j - 1]));
                }

                if (maxGap > Math.Max(pitchX * 3, gapMinimum * .5))
                {
                    continue;
                }

                return cap;
            }

            return null;
        }

        var holes = new List<DetectedHole>();
        var partialHoles = new List<DetectedHole>();
        int rejectedExterior = 0, rejectedSmall = 0;
        foreach (var items in groups)
        {
            int rowCount = items.Select(e => e.Key).Distinct().Count();
            if (rowCount < options.MinimumScanlines || items[^1].Y - items[0].Y < pitchY * (options.MinimumScanlines - 1) * .9)
            {
                rejectedSmall++;
                continue;
            }

            var lower = CapFor(items[0], -1);
            var upper = CapFor(items[^1], 1);
            if (lower is null && upper is null)
            {
                rejectedExterior++;
                continue;
            }

            // Distinct preserves first occurrence, like JS Set.
            var indices = items.SelectMany(e => new[] { e.Left, e.Right }).Concat(lower ?? []).Concat(upper ?? []).Distinct();
            var boundary = indices.Select(i => Point(positions, plane, i)).ToArray();
            var box = Box(boundary.Select(p => (p.UMm, p.VMm)));
            var u = (box.MinUMm + box.MaxUMm) / 2;
            var v = (box.MinVMm + box.MaxVMm) / 2;
            boundary = boundary.OrderBy(p => Math.Atan2(p.VMm - v, p.UMm - u)).ToArray();
            var widths = items.Select(e =>
            {
                var a = Point(positions, plane, e.Left);
                var b = Point(positions, plane, e.Right);
                return Math.Sqrt((a.UMm - b.UMm) * (a.UMm - b.UMm) + (a.VMm - b.VMm) * (a.VMm - b.VMm));
            }).ToArray();
            var center = plane.Origin.Select((x, j) => x + u * plane.UAxis[j] + v * plane.VAxis[j]).ToArray();
            bool closed = lower is not null && upper is not null;
            var hole = new DetectedHole
            {
                FaceId = plane.Id,
                Status = closed ? "CLOSED" : "PARTIAL",
                Reason = closed ? null : "One end has no measured closing rim; the opening may extend outside the scan.",
                U = u,
                V = v,
                X = center[0],
                Y = center[1],
                Z = center[2],
                Scanlines = rowCount,
                EdgePointCount = boundary.Length,
                PerimeterPoints = boundary,
                BoundaryBox = box,
                OpeningWidthRangeMm = new OpeningWidthRange(widths.Min(), widths.Max(), Median(widths)),
                Closure = new HoleClosure(lower is not null, upper is not null),
                EdgePoints = items.Select(e => new HoleEdgePair(Point(positions, plane, e.Left), Point(positions, plane, e.Right), e.Y)).ToArray(),
            };
            (closed ? holes : partialHoles).Add(hole);
        }

        return (holes, partialHoles, new HoleDiagnostics(plane.Id, assignedCount, events.Count, groups.Count, rejectedExterior, rejectedSmall, rejectedSurfaceCutoffs));
    }

    /// <summary>由 float XYZ 偵測孔洞。</summary>
    /// <remarks>來自 Hank PlyProcessor.Holes 的 <c>PlyProcessor.Holes.OrganizedHoleDetector</c>。</remarks>
    /// <param name="positions">交錯 XYZ。</param>
    /// <param name="count">點數；null 用陣列長度。</param>
    /// <param name="options">參數。</param>
    public static HoleDetectionResult Detect(float[] positions, int? count = null, HoleDetectionOptions? options = null)
    {
        return Detect(Array.ConvertAll(positions, x => (double)x), count, options);
    }

    /// <summary>由 double XYZ 偵測孔洞。</summary>
    /// <remarks>來自 Hank PlyProcessor.Holes 的 <c>PlyProcessor.Holes.OrganizedHoleDetector</c>。</remarks>
    /// <param name="positions">交錯 XYZ。</param>
    /// <param name="count">點數；null 用陣列長度。</param>
    /// <param name="options">參數。</param>
    public static HoleDetectionResult Detect(double[] positions, int? count = null, HoleDetectionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(positions);
        int n = Math.Min(count ?? positions.Length / 3, positions.Length / 3);
        if (n < 100)
        {
            throw new ArgumentException("點數不足，無法分析孔洞。");
        }

        for (int i = 0; i < n * 3; i++)
        {
            if (!double.IsFinite(positions[i]))
            {
                throw new ArgumentException("點雲包含無效座標。");
            }
        }

        options ??= new HoleDetectionOptions();
        var xs = Enumerable.Range(0, n).Select(i => positions[i * 3]).ToArray();
        var ys = Enumerable.Range(0, n).Select(i => positions[i * 3 + 1]).ToArray();
        var pitchX = Pitch(xs);
        var pitchY = Pitch(ys);
        int rowCount = ys.Select(Key).Distinct().Count();
        if (pitchX == 0 || pitchY == 0 || rowCount > n / 8.0)
        {
            throw new ArgumentException("點雲未保留規則掃描列，請使用掃描器座標的 PLY 檔案。");
        }

        var faces = FindFaces(positions, n, options);
        if (faces.Length == 0)
        {
            throw new ArgumentException("找不到有足夠量測點的鋼面。");
        }

        var faceRows = faces.Select(_ => new Dictionary<double, List<int>>()).ToArray();
        var facePoints = faces.Select(_ => new List<int>()).ToArray();
        var rawRows = new Dictionary<double, List<int>>();
        for (int i = 0; i < n; i++)
        {
            var key = Key(positions[i * 3 + 1]);
            if (!rawRows.ContainsKey(key))
            {
                rawRows.Add(key, []);
            }

            rawRows[key].Add(i);
        }

        foreach (var key in rawRows.Keys.ToArray())
        {
            rawRows[key] = rawRows[key].OrderBy(i => positions[i * 3]).ToList();
        }

        for (int i = 0; i < n; i++)
        {
            int selected = -1;
            var best = options.SurfaceToleranceMm;
            for (int j = 0; j < faces.Length; j++)
            {
                var distance = Math.Abs(Distance(faces[j], positions, i));
                if (distance < best)
                {
                    best = distance;
                    selected = j;
                }
            }

            if (selected < 0)
            {
                continue;
            }

            var key = Key(positions[i * 3 + 1]);
            var rows = faceRows[selected];
            if (!rows.ContainsKey(key))
            {
                rows.Add(key, []);
            }

            rows[key].Add(i);
            facePoints[selected].Add(i);
        }

        var holes = new List<DetectedHole>();
        var partialHoles = new List<DetectedHole>();
        var diagnostics = new List<HoleDiagnostics>();
        for (int j = 0; j < faces.Length; j++)
        {
            var r = ScanFace(positions, faces[j], faceRows[j], rawRows, pitchX, pitchY, options, facePoints[j].Count);
            holes.AddRange(r.Holes);
            partialHoles.AddRange(r.PartialHoles);
            diagnostics.Add(r.Diagnostics);
        }

        var orderedHoles = holes.OrderBy(h => h.Y).ThenBy(h => h.X).ToArray();
        var orderedPartial = partialHoles.OrderBy(h => h.Y).ThenBy(h => h.X).ToArray();
        for (int i = 0; i < orderedHoles.Length; i++)
        {
            orderedHoles[i].Id = $"H{i + 1:00}";
        }

        for (int i = 0; i < orderedPartial.Length; i++)
        {
            orderedPartial[i].Id = $"P{i + 1:00}";
        }

        var views = faces.Select((plane, j) =>
        {
            var samples = facePoints[j].Select(i =>
            {
                var p = Project(plane, positions[i * 3], positions[i * 3 + 1], positions[i * 3 + 2]);
                return new FaceSample(p.U, p.V);
            }).ToArray();
            var bounds = Box(samples.Select(p => (p.U, p.V)));
            return new HoleFaceView(
                plane.Id,
                plane,
                samples,
                bounds.MinUMm,
                bounds.MaxUMm,
                bounds.MinVMm,
                bounds.MaxVMm,
                orderedHoles.Where(h => h.FaceId == plane.Id).ToArray(),
                orderedPartial.Where(h => h.FaceId == plane.Id).ToArray());
        }).ToArray();
        return new HoleDetectionResult
        {
            PointCount = n,
            ScannerPitch = new ScannerPitch(pitchX, pitchY),
            Options = options,
            Faces = faces,
            Views = views,
            Holes = orderedHoles,
            PartialHoles = orderedPartial,
            Diagnostics = diagnostics.ToArray(),
        };
    }
}
