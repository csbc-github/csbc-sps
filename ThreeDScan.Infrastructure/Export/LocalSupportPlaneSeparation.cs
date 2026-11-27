using ThreeDScan.Domain.Models;
using ThreeDScan.Infrastructure.Export.Models;

namespace ThreeDScan.Infrastructure.Export;

/// <summary>Separates measured high objects from a broad raised support without altering the global floor or raw coordinates.</summary>
internal static class LocalSupportPlaneSeparation
{
    internal static LocalSupportResult Find(ScanCloud cloud, ScanVector[] parentPoints, int[] parentRawIndices,
        ScanCalibration floor, ExtractionOptions extractionOptions, double maxRms, CancellationToken ct = default,
        PlaneFit[]? planeCandidates = null)
    {
        ct.ThrowIfCancellationRequested();
        if (parentPoints.Length != parentRawIndices.Length) throw new ArgumentException("父物件點與來源索引長度不一致。");
        if (!double.IsFinite(maxRms) || maxRms <= 0 || !double.IsFinite(extractionOptions.CellMm)
            || extractionOptions.CellMm <= 0 || !double.IsFinite(extractionOptions.ZThresholdMm)
            || extractionOptions.ZThresholdMm <= 0 || extractionOptions.MinimumComponentCells < 1)
            throw new ArgumentException("局部承載面分離參數無效。");
        LocalSupportResult Missing(string reason) => new(new LocalSupportDiagnostics
        { ParentPointCount = parentPoints.Length, SeedThresholdMm = extractionOptions.ZThresholdMm, Reason = reason }, []);
        if (parentPoints.Length < 500) return Missing("父物件不足 500 點，未建立局部承載面。");

        var floorNormal = new ScanVector(-floor.GroundA, -floor.GroundB, 1).Normalize();
        var planes = planeCandidates ?? WingPlaneSegmentation.FindPlanes(parentPoints, Math.Clamp(maxRms * 1.5, .25, 1), 4, ct);
        var support = planes.Where(p => p.Rms <= maxRms && p.Indices.Length >= parentPoints.Length * .65
            && p.Normal.Dot(floorNormal) >= Math.Cos(15 * Math.PI / 180))
            .OrderByDescending(p => p.Indices.Length).FirstOrDefault();
        if (support is null) return Missing("未找到占父物件至少 65%、且與全域地面方向相差不超過 15° 的承載板面。");
        double below = parentPoints.Count(p => support.Normal.Dot(p) - support.D < -Math.Max(.75, support.Rms * 3)) / (double)parentPoints.Length;
        if (below > .10) return Missing("主要板面下方另有顯著量測，不能將物件頂面當成局部承載面。");
        var allowed = new bool[cloud.Points.Length];
        for (int i = 0; i < parentRawIndices.Length; i++)
        {
            if ((i & 65535) == 0) ct.ThrowIfCancellationRequested();
            int index = parentRawIndices[i];
            if ((uint)index >= cloud.Points.Length || cloud.Points[index].SourcePointIndex != index)
                throw new ArgumentException("父物件來源索引不符合原始點雲。");
            allowed[index] = true;
        }
        // Keep the existing vertical-height convention for both seed selection
        // and recovery. Recovery independently converts clearance to normal mm.
        double a = -support.Normal.X / support.Normal.Z, b = -support.Normal.Y / support.Normal.Z;
        double c = support.D / support.Normal.Z;
        var local = new ScanCalibration(0, 0, false, null, new(), new(1, 0, 0), new(0, 1, 0), new(0, 0, 1),
            a, b, c, 0, "LocalRaisedSupportPlane", [], support.Indices.Length, support.Rms, ScanBounds.Of(parentPoints));
        var cells = new Dictionary<(long X, long Y), List<int>>();
        var highSeed = new bool[cloud.Points.Length];
        double normalizer = Math.Sqrt(1 + a * a + b * b);
        double clearance = Math.Max(.35, support.Rms * 3);
        int residualCount = 0;
        int seedCount = 0;
        for (int i = 0; i < parentPoints.Length; i++)
        {
            if ((i & 65535) == 0) ct.ThrowIfCancellationRequested();
            var p = parentPoints[i];
            double height = local.Transform(p).Z;
            if (height / normalizer <= clearance) continue;
            var cell = ((long)Math.Floor(p.X / extractionOptions.CellMm), (long)Math.Floor(p.Y / extractionOptions.CellMm));
            if (!cells.TryGetValue(cell, out var indices)) cells[cell] = indices = [];
            int index = parentRawIndices[i]; indices.Add(index); residualCount++;
            if (height > extractionOptions.ZThresholdMm) { highSeed[index] = true; seedCount++; }
        }
        var unseen = cells.Keys.ToHashSet();
        var groups = new List<(int CellCount, int[] Members, int[] HighSeeds)>();
        while (unseen.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var seed = unseen.First(); unseen.Remove(seed);
            var queue = new Queue<(long X, long Y)>(); queue.Enqueue(seed);
            int cellCount = 0; var members = new List<int>();
            while (queue.TryDequeue(out var cell))
            {
                if ((cellCount++ & 1023) == 0) ct.ThrowIfCancellationRequested();
                members.AddRange(cells[cell]);
                foreach (var next in new[] { (cell.X - 1, cell.Y), (cell.X + 1, cell.Y), (cell.X, cell.Y - 1), (cell.X, cell.Y + 1) })
                    if (unseen.Remove(next)) queue.Enqueue(next);
            }
            var seeds = members.Where(i => highSeed[i]).ToArray();
            // Low measured wings may turn through a corner and never appear in
            // the high aperture. Connectivity uses real support-cleared points;
            // every retained object still needs >=100 original high seeds.
            if (cellCount >= extractionOptions.MinimumComponentCells && seeds.Length >= 100)
                groups.Add((cellCount, members.ToArray(), seeds));
        }
        groups = groups.OrderByDescending(g => g.CellCount).ThenByDescending(g => g.Members.Length)
            .ThenBy(g => g.Members.Min()).ToList();
        var claimed = new bool[cloud.Points.Length];
        foreach (var group in groups) foreach (int i in group.Members) claimed[i] = true;
        var children = new List<LocalSupportChild>();
        var details = new List<LocalSupportChildDiagnostic>();
        int rank = 0;
        foreach (var group in groups)
        {
            ct.ThrowIfCancellationRequested(); rank++;
            var indices = new List<int>(extractionOptions.RecoverMeasuredEdges ? group.Members : group.HighSeeds);
            if (extractionOptions.RecoverMeasuredEdges)
            {
                var recovered = MeasuredSurfaceRecovery.Recover(cloud, group.HighSeeds, local, extractionOptions, ct, allowed);
                foreach (int index in recovered)
                    if (allowed[index] && !claimed[index]) indices.Add(index);
            }
            var rawIndices = indices.Distinct().Order().ToArray();
            var points = rawIndices.Select(i => cloud.Points[i].Position).ToArray();
            GeometryFitting.TryFitWings(points, rawIndices, maxRms, out var fit, out var reason, ct);
            details.Add(new LocalSupportChildDiagnostic { ChildRank = rank, SeedPointCount = group.HighSeeds.Length,
                RecoveredPointCount = rawIndices.Length - group.HighSeeds.Length, CellCount = group.CellCount,
                FinalPointCount = rawIndices.Length, FitAccepted = fit is not null, Reason = reason });
            if (fit is null) continue;
            foreach (int index in rawIndices) claimed[index] = true;
            children.Add(new LocalSupportChild(rank, points, rawIndices, fit, group.CellCount));
        }
        return new LocalSupportResult(new LocalSupportDiagnostics
        {
            Detected = true, Reason = children.Count == 0 ? "承載面已辨識，但上方沒有通過完整兩翼檢查的子物件。" : null,
            ParentPointCount = parentPoints.Length, SupportPointCount = support.Indices.Length,
            SupportFraction = support.Indices.Length / (double)parentPoints.Length,
            Normal = new Point3 { X = support.Normal.X, Y = support.Normal.Y, Z = support.Normal.Z },
            PlaneD = support.D, RmsMm = support.Rms,
            AngleToFloorDegrees = Math.Acos(Math.Clamp(support.Normal.Dot(floorNormal), -1, 1)) * 180 / Math.PI,
            BelowSupportFraction = below, SeedThresholdMm = extractionOptions.ZThresholdMm, HighSeedCount = seedCount,
            ResidualClearanceMm = clearance, ResidualPointCount = residualCount,
            SeedComponentCount = groups.Count, ValidChildCount = children.Count, Children = details.ToArray(),
        }, children.ToArray());
    }
}

internal sealed record LocalSupportResult(LocalSupportDiagnostics Diagnostics, LocalSupportChild[] Children);
internal sealed record LocalSupportChild(int ChildRank, ScanVector[] Points, int[] RawIndices, WingFit Fit, int CellCount);
