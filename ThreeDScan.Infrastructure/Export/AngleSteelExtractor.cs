using ThreeDScan.Infrastructure.Export.Models;

namespace ThreeDScan.Infrastructure.Export;

/// <summary>以分區地面平面分離物件，保留原始來源點索引。</summary>
/// <remarks>地面校正僅用於分離；實際幾何與輸出仍取原始掃描器點。</remarks>
public static class AngleSteelExtractor
{
    /// <summary>由 PLY 路徑抽取。</summary>
    /// <remarks>分區地面估計與同板面恢復只用於選點，不改原始掃描器幾何。</remarks>
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
    /// <remarks>分區地面估計與同板面恢復只用於選點，不改原始掃描器幾何。</remarks>
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
        Validate(cloud, options, cancellationToken);
        void Log(string message)
        {
            cancellationToken.ThrowIfCancellationRequested();
            log?.Invoke(message);
        }

        var warnings = new List<string>();
        Log("[1/4] 保留掃描器 XY，分區取樣可見地面…");
        var ground = GroundPlaneEstimator.Estimate(cloud.Points, options, cancellationToken);
        Log($"[2/4] 多區塊地面支持 {ground.Diagnostics.SupportFraction:P1}；RMS {ground.Rmse:F3} mm");
        if (ground.Diagnostics.Warning is { } warning) warnings.Add(warning);
        Log($"[3/4] 地面 z = {ground.A:G8}x + {ground.B:G8}y + {ground.C:G8}；僅供分離");
        var calibration = new ScanCalibration(
            ground.Bounds.MaxX, ground.Bounds.MaxY, false, null,
            new ScanVector(), new ScanVector(1, 0, 0), new ScanVector(0, 1, 0), new ScanVector(0, 0, 1),
            ground.A, ground.B, ground.C, 0, "DistributedPerimeterRansac",
            ground.CornerCounts, ground.Diagnostics.SampleCount, ground.Rmse, ground.Bounds)
        {
            GroundDiagnostics = ground.Diagnostics,
        };
        Log("[4/4] 轉換、依高度過濾並分離物件…");
        var above = new List<CalibratedScanPoint>();
        // Organized captures reuse the same X/Y coordinates across rows.
        // Memoize only this call's identical bit patterns; arbitrary point
        // clouds fall back to the same quantizer after the bounded cache fills.
        var planarQuantization = new Dictionary<long, double>(4096);
        double QuantizePlanar(double value)
        {
            long bits = BitConverter.DoubleToInt64Bits(value);
            if (planarQuantization.TryGetValue(bits, out double quantized)) return quantized;
            quantized = ScanPly.Quantize(value);
            if (planarQuantization.Count < 8192) planarQuantization.Add(bits, quantized);
            return quantized;
        }
        ScanVector QuantizePosition(ScanVector point) => new(
            QuantizePlanar(point.X), QuantizePlanar(point.Y), ScanPly.Quantize(point.Z));
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
                above.Add(new CalibratedScanPoint(raw.SourcePointIndex, QuantizePosition(p), raw.ExtraColumns));
            }
        }

        var points = above.ToArray();
        int seedPointCount = points.Length;
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
            var component = FloodFill(occupied, seed, cancellationToken);
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
            if ((i & 65535) == 0) cancellationToken.ThrowIfCancellationRequested();
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

        if (options.RecoverMeasuredEdges && components.Any(cmp => cmp.Retained))
        {
            // Existing high seeds of every component are reserved before growth;
            // low measured points can only be owned by one retained component.
            var claimed = new bool[cloud.Points.Length];
            foreach (var seed in points) claimed[seed.SourcePointIndex] = true;
            for (int index = 0; index < components.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var component = components[index];
                if (!component.Retained) continue;
                var seeds = component.AbovePointIndices.Select(i => points[i].SourcePointIndex).ToArray();
                var recovered = MeasuredSurfaceRecovery.Recover(cloud, seeds, calibration, options, cancellationToken);
                var componentMembers = new List<int>(component.AbovePointIndices);
                foreach (int rawIndex in recovered)
                {
                    if (claimed[rawIndex]) continue;
                    claimed[rawIndex] = true;
                    var raw = cloud.Points[rawIndex];
                    componentMembers.Add(above.Count);
                    above.Add(new CalibratedScanPoint(rawIndex, QuantizePosition(calibration.Transform(raw.Position)), raw.ExtraColumns));
                }
                int added = componentMembers.Count - component.PointCount;
                if (added == 0) continue;
                components[index] = new ExtractionComponent(
                    component.Label, component.Rank, component.CellCount, componentMembers.Count,
                    component.Retained, component.Name, componentMembers.ToArray(),
                    ScanBounds.Of(componentMembers.Select(i => above[i].Position)));
                warnings.Add($"rank {component.Rank}：{seeds.Length:N0} 個高度種子恢復 {added:N0} 個連續同板面的實測低邊緣點；排除地面且未插值。");
            }
            points = above.ToArray();
        }

        Log($"完成：{cloud.Points.Length:N0} 點 → 高度種子 {seedPointCount:N0} 點、恢復 {points.Length - seedPointCount:N0} 點；取出 {components.Count(cmp => cmp.Retained)} 個物件。");
        return new ExtractionResult
        {
            Source = cloud,
            AbovePoints = points,
            SeedPointCount = seedPointCount,
            Calibration = calibration,
            Options = options,
            OccupiedCellCount = occupied.Count,
            Components = components,
            Warnings = warnings.ToArray(),
        };
    }

    private static HashSet<GridCell> FloodFill(CompatibleGridSet occupied, GridCell seed, CancellationToken cancellationToken)
    {
        var visited = new HashSet<GridCell>();
        var queue = new Stack<GridCell>();
        queue.Push(seed);
        while (queue.TryPop(out var cell))
        {
            if ((visited.Count & 16383) == 0) cancellationToken.ThrowIfCancellationRequested();
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

    private static void Validate(ScanCloud cloud, ExtractionOptions options, CancellationToken cancellationToken)
    {
        if (cloud.Points.Length == 0)
        {
            throw new ArgumentException("點雲沒有有效頂點。");
        }

        if (!double.IsFinite(options.ZThresholdMm) || !double.IsFinite(options.CellMm) || options.CellMm <= 0
            || options.MinimumComponentCells < 1 || !double.IsFinite(options.CornerWindowMm) || options.CornerWindowMm <= 0
            || options.CornerSample < 1 || options.GroundMaxSamples < 30
            || !double.IsFinite(options.GroundDistanceToleranceMm) || options.GroundDistanceToleranceMm <= 0
            || !double.IsFinite(options.GroundMaximumSlope) || options.GroundMaximumSlope <= 0
            || !double.IsFinite(options.GroundMinimumSupportFraction)
            || options.GroundMinimumSupportFraction <= 0 || options.GroundMinimumSupportFraction > 1)
        {
            throw new ArgumentException("抽取門檻、格子、地面取樣與品質設定無效。");
        }

        for (int i = 0; i < cloud.Points.Length; i++)
        {
            if ((i & 65535) == 0) cancellationToken.ThrowIfCancellationRequested();
            var p = cloud.Points[i];
            if (p.SourcePointIndex != i || !double.IsFinite(p.Position.X) || !double.IsFinite(p.Position.Y) || !double.IsFinite(p.Position.Z))
            {
                throw new ArgumentException("點雲的座標或來源點索引無效。");
            }
        }
    }
}
