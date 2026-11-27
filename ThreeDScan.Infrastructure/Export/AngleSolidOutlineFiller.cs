using ThreeDScan.Infrastructure.Export.Models;

namespace ThreeDScan.Infrastructure.Export;

/// <summary>在各模型實體原有外框內補齊鋸齒缺格，保留封閉內孔及分離的主要實體。</summary>
public static class AngleSolidOutlineFiller
{
    private const double MaximumExcludedVolumeFraction = .001;
    private const double MaximumIslandCrossMm = 5;
    private const double MaximumIslandAlongMm = 20;

    /// <summary>回傳另建的補平模型；來源模型、量測點雲及已保存量測均不修改。</summary>
    public static SolidModel Fill(SolidModel source, double minimumNotchDepthMm = 10, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!double.IsFinite(minimumNotchDepthMm) || minimumNotchDepthMm < 0)
            throw new ArgumentOutOfRangeException(nameof(minimumNotchDepthMm));
        cancellationToken.ThrowIfCancellationRequested();
        var before = source.Validate();
        if (source.Reconstructions.Count == 0)
            throw new InvalidDataException("外緣補平需要來源角鋼重建框架。");

        double cell = source.Options.CellMm;
        var bodies = source.Triangles.GroupBy(t => t.Solid)
            .Select(g => ReadBody(source, g.Key, g.ToArray(), cancellationToken)).ToArray();
        var islands = bodies.Where(candidate => candidate.Holes == 0
            && (candidate.C1 - candidate.C0) * cell <= MaximumIslandCrossMm + 1e-7
            && (candidate.R1 - candidate.R0) * cell <= MaximumIslandAlongMm + 1e-7
            && bodies.Any(main => main != candidate && main.FrameIndex == candidate.FrameIndex
                && main.Volume > candidate.Volume
                && candidate.R0 >= main.R0 - 1 && candidate.R1 <= main.R1 + 1
                && new[] { Math.Abs(candidate.C0 - main.C0), Math.Abs(candidate.C1 - main.C0),
                    Math.Abs(candidate.C0 - main.C1), Math.Abs(candidate.C1 - main.C1) }.Min() * cell <= MaximumIslandCrossMm + 1e-7))
            .ToArray();
        double excludedVolume = islands.Sum(b => b.Volume);
        if (excludedVolume > before.VolumeMm3 * MaximumExcludedVolumeFraction)
        {
            islands = [];
            excludedVolume = 0;
        }
        var kept = bodies.Except(islands).ToArray();
        foreach (var body in kept) FillExterior(body, minimumNotchDepthMm, cell, cancellationToken);

        // Expanding one solid's envelope must not consume a separate measured
        // solid or erase a declared gap. Conflicting envelopes leave the entire
        // source model intact rather than creating interpenetrating B-reps.
        for (int i = 0; i < kept.Length; i++)
        for (int j = i + 1; j < kept.Length; j++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (kept[i].FrameIndex != kept[j].FrameIndex || RepairedBodiesTouch(kept[i], kept[j]))
            {
                string reason = kept[i].FrameIndex != kept[j].FrameIndex
                    ? "多個主要實體使用不同重建框架，保留原模型以避免推估外框互相穿入。"
                    : "分離主要實體的補平外框會相交或接觸，保留原模型及原有間隙。";
                var unchanged = Copy(source);
                unchanged.OutlineFill = Report(false, reason, before, before, bodies, [], 0, cell, minimumNotchDepthMm);
                return unchanged;
            }
        }

        bool hasAddedCells = kept.Any(b => b.Added.Count > 0);
        bool changed = hasAddedCells || islands.Length > 0;
        if (!changed)
        {
            var unchanged = Copy(source);
            unchanged.OutlineFill = Report(false, null, before, before, kept, [], 0, cell, minimumNotchDepthMm,
                source.Reconstructions);
            return unchanged;
        }

        var result = new SolidModel { Options = source.Options };
        foreach (var body in kept) Append(result, body, source.Reconstructions[body.FrameIndex], cancellationToken);
        var filledFrames = kept.Where(b => b.Added.Count > 0).Select(b => b.FrameIndex).ToHashSet();
        for (int i = 0; i < source.Reconstructions.Count; i++)
            result.Reconstructions.Add(CopyFrame(source.Reconstructions[i], filledFrames.Contains(i)));
        var after = result.Validate();
        if (after.ClosedSolidCount != kept.Length || after.ThroughHoleCount != before.ThroughHoleCount)
            throw new InvalidDataException("外緣補平改變主要實體連通性或封閉內孔，已取消模型輸出。");
        double addedVolume = kept.Sum(b => b.Added.Count * cell * cell * source.Reconstructions[b.FrameIndex].BisectorExtrusionMm);
        double expectedVolume = before.VolumeMm3 - excludedVolume + addedVolume;
        if (Math.Abs(after.VolumeMm3 - expectedVolume) > Math.Max(.001, Math.Abs(expectedVolume) * 1e-8))
            throw new InvalidDataException("外緣補平體積與推估缺格體積不一致。");
        result.OutlineFill = Report(true, null, before, after, kept, islands, excludedVolume, cell, minimumNotchDepthMm,
            source.Reconstructions);
        return result;
    }

    private static Body ReadBody(SolidModel model, int solid, SolidTriangle[] triangles, CancellationToken ct)
    {
        var top = triangles.Where(t => (t.A & 1) == 0 && (t.B & 1) == 0 && (t.C & 1) == 0).ToArray();
        if (top.Length == 0) throw new InvalidDataException("外緣補平找不到來源折板上表面。");
        var indices = top.SelectMany(t => new[] { t.A, t.B, t.C }).Distinct().ToArray();
        double cell = model.Options.CellMm;
        int frameIndex = -1;
        Dictionary<int, (int R, int C)>? coordinate = null;
        for (int f = 0; f < model.Reconstructions.Count; f++)
        {
            ct.ThrowIfCancellationRequested();
            var frame = model.Reconstructions[f];
            var grid = new Dictionary<int, (int R, int C)>(indices.Length);
            bool matches = true;
            foreach (int i in indices)
            {
                var d = model.Vertices[i] - frame.Origin;
                int r = (int)Math.Round(d.Dot(frame.LongAxis) / cell), c = (int)Math.Round(d.Dot(frame.CrossAxis) / cell);
                var expected = frame.Origin + frame.CrossAxis.Scale(c * cell) + frame.LongAxis.Scale(r * cell)
                    + frame.UpAxis.Scale(-frame.FlangeSlope * Math.Abs(c * cell));
                if ((model.Vertices[i] - expected).Length > Math.Max(1e-6, cell * 1e-6)
                    || i + 1 >= model.Vertices.Count
                    || (model.Vertices[i + 1] - (expected - frame.UpAxis.Scale(frame.BisectorExtrusionMm))).Length > Math.Max(1e-6, cell * 1e-6))
                {
                    matches = false;
                    break;
                }
                grid[i] = (r, c);
            }
            if (matches) { frameIndex = f; coordinate = grid; break; }
        }
        if (frameIndex < 0 || coordinate is null)
            throw new InvalidDataException("外緣補平來源頂點不符合規則折板網格。");
        var cells = new HashSet<(int R, int C)>();
        foreach (var t in top)
        {
            var a = coordinate[t.A]; var b = coordinate[t.B]; var c = coordinate[t.C];
            int r = Math.Min(a.R, Math.Min(b.R, c.R)), column = Math.Min(a.C, Math.Min(b.C, c.C));
            if (Math.Max(a.R, Math.Max(b.R, c.R)) - r != 1 || Math.Max(a.C, Math.Max(b.C, c.C)) - column != 1)
                throw new InvalidDataException("外緣補平來源上表面不是完整網格三角形。");
            cells.Add((r, column));
        }
        if (top.Length != 2 * cells.Count) throw new InvalidDataException("外緣補平來源格數與三角形數不一致。");
        var reference = model.Reconstructions[frameIndex].Origin;
        double volume = triangles.Sum(t => (model.Vertices[t.A] - reference)
            .Dot((model.Vertices[t.B] - reference).Cross(model.Vertices[t.C] - reference)) / 6);
        var vertices = triangles.SelectMany(t => new[] { t.A, t.B, t.C }).ToHashSet();
        var edges = triangles.SelectMany(t => new[] { Edge(t.A, t.B), Edge(t.B, t.C), Edge(t.C, t.A) }).ToHashSet();
        int holes = (2 - (vertices.Count - edges.Count + triangles.Length)) / 2;
        return new Body(solid, frameIndex, cells, volume, holes);
    }

    private static (int, int) Edge(int a, int b) => a < b ? (a, b) : (b, a);

    private static void FillExterior(Body body, double minimumDepth, double cell, CancellationToken ct)
    {
        if ((long)(body.R1 - body.R0) * (body.C1 - body.C0) > 600000)
            throw new InvalidDataException("外緣補平網格範圍過大。");
        var queue = new Queue<(int R, int C)>();
        void Visit(int r, int c)
        {
            if (r < body.R0 || r >= body.R1 || c < body.C0 || c >= body.C1
                || body.Cells.Contains((r, c)) || !body.Exterior.Add((r, c))) return;
            queue.Enqueue((r, c));
        }
        for (int r = body.R0; r < body.R1; r++) { Visit(r, body.C0); Visit(r, body.C1 - 1); }
        for (int c = body.C0; c < body.C1; c++) { Visit(body.R0, c); Visit(body.R1 - 1, c); }
        int visited = 0;
        while (queue.TryDequeue(out var p))
        {
            if ((visited++ & 4095) == 0) ct.ThrowIfCancellationRequested();
            Visit(p.R - 1, p.C); Visit(p.R + 1, p.C); Visit(p.R, p.C - 1); Visit(p.R, p.C + 1);
        }
        if (minimumDepth == 0)
        {
            body.Added.UnionWith(body.Exterior);
            return;
        }

        // Evaluate all four edge directions against the untouched source mask.
        // Filling one row never makes a neighbouring short notch exceed the gate.
        var rows = body.Cells.GroupBy(p => p.R).ToDictionary(g => g.Key, g => (Low: g.Min(p => p.C), High: g.Max(p => p.C)));
        var cols = body.Cells.GroupBy(p => p.C).ToDictionary(g => g.Key, g => (Low: g.Min(p => p.R), High: g.Max(p => p.R)));
        void AddIfExterior(int r, int c) { if (body.Exterior.Contains((r, c))) body.Added.Add((r, c)); }
        foreach (var (r, range) in rows)
        {
            ct.ThrowIfCancellationRequested();
            if ((range.Low - body.C0) * cell > minimumDepth)
                for (int c = body.C0; c < range.Low; c++) AddIfExterior(r, c);
            if ((body.C1 - 1 - range.High) * cell > minimumDepth)
                for (int c = range.High + 1; c < body.C1; c++) AddIfExterior(r, c);
        }
        foreach (var (c, range) in cols)
        {
            ct.ThrowIfCancellationRequested();
            if ((range.Low - body.R0) * cell > minimumDepth)
                for (int r = body.R0; r < range.Low; r++) AddIfExterior(r, c);
            if ((body.R1 - 1 - range.High) * cell > minimumDepth)
                for (int r = range.High + 1; r < body.R1; r++) AddIfExterior(r, c);
        }

        // A deep repair can close the entrance to an originally exterior void.
        // Keep the original enclosed holes untouched; only complete such newly
        // enclosed exterior pockets, so the repair cannot manufacture a new hole.
        var stillExterior = new HashSet<(int R, int C)>();
        void VisitRemaining(int r, int c)
        {
            if (r < body.R0 || r >= body.R1 || c < body.C0 || c >= body.C1
                || body.Cells.Contains((r, c)) || body.Added.Contains((r, c)) || !stillExterior.Add((r, c))) return;
            queue.Enqueue((r, c));
        }
        for (int r = body.R0; r < body.R1; r++) { VisitRemaining(r, body.C0); VisitRemaining(r, body.C1 - 1); }
        for (int c = body.C0; c < body.C1; c++) { VisitRemaining(body.R0, c); VisitRemaining(body.R1 - 1, c); }
        visited = 0;
        while (queue.TryDequeue(out var p))
        {
            if ((visited++ & 4095) == 0) ct.ThrowIfCancellationRequested();
            VisitRemaining(p.R - 1, p.C); VisitRemaining(p.R + 1, p.C);
            VisitRemaining(p.R, p.C - 1); VisitRemaining(p.R, p.C + 1);
        }
        foreach (var p in body.Exterior)
            if (!body.Added.Contains(p) && !stillExterior.Contains(p)) { body.Added.Add(p); body.FilledPocketCells++; }
    }

    private static bool RepairedBodiesTouch(Body a, Body b)
    {
        if (a.C0 > b.C1 || b.C0 > a.C1 || a.R0 > b.R1 || b.R0 > a.R1) return false;
        var occupiedB = new HashSet<(int R, int C)>(b.Cells);
        occupiedB.UnionWith(b.Added);
        foreach (var p in a.Cells.Concat(a.Added))
            for (int dr = -1; dr <= 1; dr++)
                for (int dc = -1; dc <= 1; dc++)
                    if (occupiedB.Contains((p.R + dr, p.C + dc))) return true;
        return false;
    }

    private static void Append(SolidModel model, Body body, SolidReconstruction frame, CancellationToken ct)
    {
        double cell = model.Options.CellMm;
        var occupied = new HashSet<(int R, int C)>(body.Cells);
        occupied.UnionWith(body.Added);
        var nodes = new Dictionary<(int, int), (int Top, int Bottom)>();
        var groups = new Dictionary<(int Kind, int Coordinate), int>();
        (int Top, int Bottom) Node(int r, int c)
        {
            if (nodes.TryGetValue((r, c), out var node)) return node;
            double u = c * cell, v = r * cell, w = -frame.FlangeSlope * Math.Abs(u);
            int index = model.Vertices.Count;
            var p = frame.Origin + frame.CrossAxis.Scale(u) + frame.LongAxis.Scale(v) + frame.UpAxis.Scale(w);
            model.Vertices.Add(p);
            model.Vertices.Add(p - frame.UpAxis.Scale(frame.BisectorExtrusionMm));
            return nodes[(r, c)] = (index, index + 1);
        }
        void Tri(int a, int b, int c, int kind, int at = 0)
        {
            if (!groups.TryGetValue((kind, at), out int group))
            {
                groups[(kind, at)] = group = model.Planes.Count;
                var p = model.Vertices[a];
                model.Planes.Add(new(p, (model.Vertices[b] - p).Cross(model.Vertices[c] - p).Normalize()));
            }
            model.Triangles.Add(new(a, b, c, group, body.Solid));
        }
        for (int r = body.R0; r < body.R1; r++)
        {
            ct.ThrowIfCancellationRequested();
            for (int c = body.C0; c < body.C1; c++)
            {
                if (!occupied.Contains((r, c))) continue;
                int wing = c < 0 ? 0 : 1;
                var a = Node(r, c); var b = Node(r, c + 1); var d = Node(r + 1, c); var e = Node(r + 1, c + 1);
                Tri(a.Top, b.Top, e.Top, wing); Tri(a.Top, e.Top, d.Top, wing);
                Tri(a.Bottom, e.Bottom, b.Bottom, wing + 2); Tri(a.Bottom, d.Bottom, e.Bottom, wing + 2);
                void Wall((int Top, int Bottom) p, (int Top, int Bottom) q, int kind, int at)
                {
                    Tri(p.Top, q.Bottom, q.Top, kind, at); Tri(p.Top, p.Bottom, q.Bottom, kind, at);
                }
                if (!occupied.Contains((r - 1, c))) Wall(a, b, 4, r);
                if (!occupied.Contains((r, c + 1))) Wall(b, e, 5, c + 1);
                if (!occupied.Contains((r + 1, c))) Wall(e, d, 6, r + 1);
                if (!occupied.Contains((r, c - 1))) Wall(d, a, 7, c);
            }
        }
    }

    private static OutlineFillReport Report(bool applied, string? skip, SolidTopology before, SolidTopology after,
        Body[] kept, Body[] islands, double excludedVolume, double cell, double minimumDepth, IReadOnlyList<SolidReconstruction>? frames = null) => new()
    {
        Applied = applied, SkipReason = skip, SourceSolidCount = before.ClosedSolidCount,
        MinimumNotchDepthMm = minimumDepth,
        FinalSolidCount = after.ClosedSolidCount, SourceThroughHoleCount = before.ThroughHoleCount,
        FinalThroughHoleCount = after.ThroughHoleCount, ExcludedSmallIslandCount = islands.Length,
        ExcludedSmallIslandVolumeMm3 = excludedVolume,
        OriginalOccupiedCells = kept.Sum(b => b.Cells.Count),
        FilledExteriorCells = applied ? kept.Sum(b => b.Added.Count) : 0,
        FilledEnclosedExteriorPocketCells = applied ? kept.Sum(b => b.FilledPocketCells) : 0,
        RetainedEnclosedHoleCells = frames is not null ? kept.Sum(b => (b.R1 - b.R0) * (b.C1 - b.C0) - b.Cells.Count - b.Exterior.Count) : 0,
        AddedProjectedAreaMm2 = applied ? kept.Sum(b => b.Added.Count) * cell * cell : 0,
        AddedVolumeMm3 = applied ? kept.Sum(b => b.Added.Count * cell * cell * frames![b.FrameIndex].BisectorExtrusionMm) : 0,
        Bodies = frames is not null ? kept.Select(b => new OutlineFillBodyReport
        {
            SourceSolid = b.Solid, ReconstructionIndex = b.FrameIndex,
            MinCrossMm = b.C0 * cell, MaxCrossMm = b.C1 * cell,
            MinAlongMm = b.R0 * cell, MaxAlongMm = b.R1 * cell,
            OriginalOccupiedCells = b.Cells.Count, FilledExteriorCells = b.Added.Count,
            FilledEnclosedExteriorPocketCells = b.FilledPocketCells,
            RetainedEnclosedHoleCells = (b.R1 - b.R0) * (b.C1 - b.C0) - b.Cells.Count - b.Exterior.Count,
            AddedProjectedAreaMm2 = b.Added.Count * cell * cell,
            AddedVolumeMm3 = b.Added.Count * cell * cell * frames![b.FrameIndex].BisectorExtrusionMm,
        }).ToArray() : [],
    };

    private static SolidModel Copy(SolidModel source)
    {
        var copy = new SolidModel { Options = source.Options };
        copy.Vertices.AddRange(source.Vertices); copy.Triangles.AddRange(source.Triangles);
        copy.Planes.AddRange(source.Planes); copy.Reconstructions.AddRange(source.Reconstructions);
        return copy;
    }

    private static SolidReconstruction CopyFrame(SolidReconstruction r, bool filled) => new(
        r.Origin, r.CrossAxis, r.LongAxis, r.UpAxis, r.FlangeSlope, r.FaceNormalThicknessMm,
        r.BisectorExtrusionMm, r.LeftPlaneRmsMm, r.RightPlaneRmsMm, r.FilledSamplingCells,
        r.RemovedAmbiguousCells, r.RemovedSmallIslandCells, r.OccupiedCells)
    {
        InputMeasurementCount = r.InputMeasurementCount,
        WingPlaneMeasurementCount = r.WingPlaneMeasurementCount,
        MeasuredRidgePointCount = r.MeasuredRidgePointCount,
        OuterEdgesFilled = filled || r.OuterEdgesFilled,
    };

    private sealed class Body(int solid, int frameIndex, HashSet<(int R, int C)> cells, double volume, int holes)
    {
        internal int Solid { get; } = solid;
        internal int FrameIndex { get; } = frameIndex;
        internal HashSet<(int R, int C)> Cells { get; } = cells;
        internal HashSet<(int R, int C)> Exterior { get; } = [];
        internal HashSet<(int R, int C)> Added { get; } = [];
        internal int FilledPocketCells { get; set; }
        internal int R0 { get; } = cells.Min(p => p.R);
        internal int R1 { get; } = cells.Max(p => p.R) + 1;
        internal int C0 { get; } = cells.Min(p => p.C);
        internal int C1 { get; } = cells.Max(p => p.C) + 1;
        internal double Volume { get; } = volume;
        internal int Holes { get; } = holes;
    }
}
