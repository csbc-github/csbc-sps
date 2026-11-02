using ThreeDScan.Infrastructure.Export.Models;

namespace ThreeDScan.Infrastructure.Export;

/// <summary>原版 process_scan.pyc process() 管線，含備援路徑。</summary>
/// <remarks>由 Hank Scan3D 的 AngleSteelExtractor 搬入，邏輯不改。</remarks>
public static class AngleSteelExtractor
{
    /// <summary>由 PLY 路徑抽取。</summary>
    /// <remarks>來自 Hank Scan3D 的 <c>Scan3D.Processing.Legacy.Extraction.AngleSteelExtractor</c>。</remarks>
    /// <param name="path">PLY 路徑。</param>
    /// <param name="options">抽取參數。</param>
    /// <param name="log">進度紀錄。</param>
    /// <param name="cancellationToken">取消。</param>
    public static ExtractionResult Process(
        string path,
        ExtractionOptions? options = null,
        Action<string>? log = null,
        CancellationToken cancellationToken = default)
    {
        return Process(ScanPly.Read(path), options, log, cancellationToken);
    }

    /// <summary>由點雲抽取。</summary>
    /// <remarks>來自 Hank Scan3D 的 <c>Scan3D.Processing.Legacy.Extraction.AngleSteelExtractor</c>。</remarks>
    /// <param name="cloud">點雲。</param>
    /// <param name="options">抽取參數。</param>
    /// <param name="log">進度紀錄。</param>
    /// <param name="cancellationToken">取消。</param>
    public static ExtractionResult Process(
        ScanCloud cloud,
        ExtractionOptions? options = null,
        Action<string>? log = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new ExtractionOptions();
        Validate(cloud, options);
        void Log(string message)
        {
            cancellationToken.ThrowIfCancellationRequested();
            log?.Invoke(message);
        }

        var warnings = new List<string>();
        Log("[1/4] 偵測掃描器邊緣並建立 XY 座標…");
        var (edgeX, edgeY) = FindEdges(cloud.Points);
        ScanVector origin = new ScanVector();
        ScanVector xa = new ScanVector(1, 0, 0);
        ScanVector ya = new ScanVector(0, 1, 0);
        ScanVector za = new ScanVector(0, 0, 1);
        bool useXY = true;
        string? edgeFallback = null;
        try
        {
            (origin, xa, ya, za) = BuildBasis(cloud.Points, edgeX, edgeY);
        }
        catch (CalibrationMathException ex)
        {
            useXY = false;
            edgeFallback = ex.Message;
            warnings.Add("未取得足夠有效邊緣，沿用原版備援：保留掃描器 XY，依 Z 高度分布校正地面。");
            Log(warnings[^1]);
        }

        ScanVector Align(ScanVector raw)
        {
            var r = raw - origin;
            return new ScanVector(r.Dot(xa), r.Dot(ya), r.Dot(za));
        }

        var bounds = ScanBounds.Of(cloud.Points.Select(p => Align(p.Position)));
        Log("[2/4] 取樣四角並校正地面…");
        double a = 0, b = 0, c = 0, zZero = 0, rmse = 0;
        int[] cornerCounts = new int[4];
        int groundSampleCount = 0;
        string groundMethod;
        if (useXY)
        {
            double w = options.CornerWindowMm;
            (double X, double Y)[] centers =
            [
                (bounds.MinX + w, bounds.MinY + w),
                (bounds.MaxX - w, bounds.MinY + w),
                (bounds.MinX + w, bounds.MaxY - w),
                (bounds.MaxX - w, bounds.MaxY - w),
            ];
            var bins = centers.Select(_ => new List<ScanVector>()).ToArray();
            for (int i = 0; i < cloud.Points.Length; i += options.CornerSample)
            {
                var p = Align(cloud.Points[i].Position);
                for (int j = 0; j < centers.Length; j++)
                {
                    if (Math.Abs(p.X - centers[j].X) < w && Math.Abs(p.Y - centers[j].Y) < w)
                    {
                        bins[j].Add(p);
                    }
                }
            }

            cornerCounts = bins.Select(bin => bin.Count).ToArray();
            var corners = bins.SelectMany(bin => bin.Take(1000)).ToList();
            groundSampleCount = corners.Count;
            groundMethod = "Corners";
            if (corners.Count >= 4)
            {
                try
                {
                    (a, b, c) = CalibrationMath.FitPlane(corners);
                }
                catch (CalibrationMathException)
                {
                    groundMethod = "DegenerateCorners";
                    warnings.Add("四角地面點退化，沿用原版備援：地面平面係數設為零。");
                }
            }
            else
            {
                groundMethod = "InsufficientCorners";
                warnings.Add("四角取樣不足四點，沿用原版備援：地面平面係數設為零。");
            }

            var residuals = corners.Select(p => p.Z - (a * p.X + b * p.Y + c)).ToArray();
            if (residuals.Length > 0)
            {
                zZero = CalibrationMath.Median(residuals);
                double sum = 0;
                foreach (var value in residuals)
                {
                    sum += (value - zZero) * (value - zZero);
                }

                rmse = Math.Sqrt(sum / residuals.Length);
            }
        }
        else
        {
            var samples = cloud.Points.Where((_, i) => i % options.CornerSample == 0).Select(p => p.Position.Z).ToArray();
            groundSampleCount = samples.Length;
            double min = samples.Min(), max = samples.Max(), width = max > min ? (max - min) / 80 : 1;
            var histogram = new int[80];
            foreach (double value in samples)
            {
                histogram[Math.Min(79, (int)((value - min) / width))]++;
            }

            int peak = Array.IndexOf(histogram, histogram.Max());
            c = min + (peak + .5) * width;
            groundMethod = "Histogram";
        }

        Log($"[3/4] Z 零點 {zZero:F6} mm；地面 z = {a:G8}x + {b:G8}y + {c:G8}");
        var calibration = new ScanCalibration(
            edgeX, edgeY, useXY, edgeFallback, origin, xa, ya, za, a, b, c, zZero,
            groundMethod, cornerCounts, groundSampleCount, rmse, bounds);
        Log("[4/4] 轉換、依高度過濾並分離物件…");
        var above = new List<CalibratedScanPoint>();
        for (int i = 0; i < cloud.Points.Length; i++)
        {
            if (i % 100000 == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            var raw = cloud.Points[i];
            var p = calibration.Transform(raw.Position);
            if (p.Z > options.ZThresholdMm)
            {
                above.Add(new CalibratedScanPoint(raw.SourcePointIndex, ScanPly.Quantize(p), raw.ExtraColumns));
            }
        }

        var points = above.ToArray();
        GridCell Cell(ScanVector p)
        {
            return new GridCell(checked((long)(p.X / options.CellMm)), checked((long)(p.Y / options.CellMm)));
        }

        var occupied = new CompatibleGridSet();
        foreach (var point in points)
        {
            occupied.Add(Cell(point.Position));
        }

        var remaining = occupied.Copy();
        var labels = new Dictionary<GridCell, int>();
        var sizes = new List<int>();
        while (remaining.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var seed = remaining.Cells.First();
            var component = FloodFill(occupied, seed);
            int label = sizes.Count + 1;
            foreach (var cell in component)
            {
                labels[cell] = label;
            }

            sizes.Add(component.Count);
            remaining.RemoveAll(component);
        }

        var members = sizes.Select(_ => new List<int>()).ToArray();
        for (int i = 0; i < points.Length; i++)
        {
            members[labels[Cell(points[i].Position)] - 1].Add(i);
        }

        var order = Enumerable.Range(0, sizes.Count).OrderByDescending(i => sizes[i]).ToArray();
        var components = order.Select((i, index) =>
        {
            int rank = index + 1;
            string name = rank == 1 ? "angle_steel" : rank == 2 ? "door_stopper" : $"object_{rank}";
            return new ExtractionComponent(
                i + 1,
                rank,
                sizes[i],
                members[i].Count,
                sizes[i] >= options.MinimumComponentCells,
                name,
                members[i].ToArray(),
                ScanBounds.Of(members[i].Select(j => points[j].Position)));
        }).ToArray();
        if (!components.Any(cmp => cmp.Retained))
        {
            warnings.Add("沒有符合高度及最小連通格數的物件，未取出角鋼。");
        }

        Log($"完成：{cloud.Points.Length:N0} 點 → 高度過濾 {points.Length:N0} 點；取出 {components.Count(cmp => cmp.Retained)} 個物件。");
        return new ExtractionResult
        {
            Source = cloud,
            AbovePoints = points,
            Calibration = calibration,
            Options = options,
            OccupiedCellCount = occupied.Count,
            Components = components,
            Warnings = warnings.ToArray(),
        };
    }

    private static HashSet<GridCell> FloodFill(CompatibleGridSet occupied, GridCell seed)
    {
        var visited = new HashSet<GridCell>();
        var queue = new Stack<GridCell>();
        queue.Push(seed);
        while (queue.TryPop(out var cell))
        {
            if (visited.Contains(cell) || !occupied.Contains(cell))
            {
                continue;
            }

            visited.Add(cell);
            foreach (var next in new[]
            {
                new GridCell(cell.X + 1, cell.Y),
                new GridCell(cell.X - 1, cell.Y),
                new GridCell(cell.X, cell.Y + 1),
                new GridCell(cell.X, cell.Y - 1),
            })
            {
                if (!visited.Contains(next))
                {
                    queue.Push(next);
                }
            }
        }

        return visited;
    }

    private static (double X, double Y) FindEdges(SourceScanPoint[] points)
    {
        var xScores = new Dictionary<double, (int Count, int Total)>();
        var yScores = new Dictionary<double, (int Count, int Total)>();
        var previousByX = new Dictionary<double, (double Y, double Z)>();
        double previousY = 0, previousZ = 0;
        foreach (var point in points)
        {
            var p = point.Position;
            double xi = Math.Round(p.X, MidpointRounding.ToEven), yi = Math.Round(p.Y, MidpointRounding.ToEven);
            var previous = previousByX.GetValueOrDefault(xi);
            var score = xScores.GetValueOrDefault(xi);
            xScores[xi] = (score.Count + (Math.Abs(p.Z - previous.Z) > .2 ? 1 : 0), score.Total + 1);
            previousByX[xi] = (p.Y, p.Z);
            if (Math.Abs(p.Y - previousY) > 1e-6)
            {
                var yScore = yScores.GetValueOrDefault(yi);
                yScores[yi] = (yScore.Count + (Math.Abs(p.Z - previousZ) > .8 ? 1 : 0), yScore.Total + 1);
            }

            previousY = p.Y;
            previousZ = p.Z;
        }

        // The bundled implementation selects max coordinate key, not the largest discontinuity score.
        return (xScores.Count > 0 ? xScores.Keys.Max() : Math.Round(points.Max(p => p.Position.X)),
            yScores.Count > 0 ? yScores.Keys.Max() : Math.Round(points.Max(p => p.Position.Y)));
    }

    private static (ScanVector Origin, ScanVector X, ScanVector Y, ScanVector Z) BuildBasis(
        SourceScanPoint[] points,
        double edgeX,
        double edgeY)
    {
        var xPoints = points.Where(p => Math.Abs(p.Position.X - edgeX) < 1e-6).Select(p => p.Position).ToArray();
        var yPoints = points.Where(p => Math.Abs(p.Position.Y - edgeY) < 1e-6).Select(p => p.Position).ToArray();
        if (xPoints.Length < 20 || yPoints.Length < 20)
        {
            throw new CalibrationMathException("Not enough edge points");
        }

        var xl = CalibrationMath.RobustLine(xPoints);
        var yl = CalibrationMath.RobustLine(yPoints);
        var origin = CalibrationMath.ClosestOrigin(xl, yl);
        var xa = xl.Direction.Normalize();
        var ya = yl.Direction.Normalize();
        if (xa.Dot(ya) < 0)
        {
            ya = ya.Scale(-1.0);
        }

        var za = xa.Cross(ya).Normalize();
        ya = za.Cross(xa).Normalize();
        return (origin, xa, ya, za);
    }

    private static void Validate(ScanCloud cloud, ExtractionOptions options)
    {
        if (cloud.Points.Length == 0)
        {
            throw new ArgumentException("點雲沒有有效頂點。");
        }

        if (!double.IsFinite(options.ZThresholdMm) || !double.IsFinite(options.CellMm) || options.CellMm <= 0
            || options.MinimumComponentCells < 1 || !double.IsFinite(options.CornerWindowMm) || options.CornerWindowMm <= 0
            || options.CornerSample < 1)
        {
            throw new ArgumentException("高度門檻必須有限；格子、最小連通格數、四角視窗與取樣步距必須大於零。");
        }

        for (int i = 0; i < cloud.Points.Length; i++)
        {
            var p = cloud.Points[i];
            if (p.SourcePointIndex != i || !double.IsFinite(p.Position.X) || !double.IsFinite(p.Position.Y) || !double.IsFinite(p.Position.Z))
            {
                throw new ArgumentException("點雲的座標或來源點索引無效。");
            }
        }
    }
}
