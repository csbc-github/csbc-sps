using System.Text.Json;
using ThreeDScan.Infrastructure.Export;
using ThreeDScan.Infrastructure.Export.Models;

internal static class OutlineFillChecks
{
    internal static void Run(Action<string, Func<object>> check, string output)
    {
        check("outline-repair-default-only-notches-deeper-than-10mm", () => Threshold(1, 9, 10, 11));
        check("outline-repair-depth-threshold-uses-mm-on-1.25mm-grid", () => Threshold(1.25, 7, 8, 9));
        check("outline-fill-straightens-edges-preserves-exact-hole-cells", () => Positive(output, 6, 1, 35, 17, 31));
        check("outline-fill-oblique-XYZ-1.25mm-grid-4.5mm-thickness", () => Positive(output, 4.5, 1.25, -58, -9, 23));
        check("outline-fill-straight-envelope-is-no-op", () => NoOp(output));
        check("outline-fill-small-edge-island-only-is-excluded", () => SmallIsland(output));
        check("outline-fill-island-proximity-uses-nearest-real-outer-edge", NearestOuterEdge);
        check("outline-fill-large-secondary-body-is-retained", LargeIsland);
        check("outline-fill-preserves-unmeasured-ridge-corridor", RidgeGap);
        check("outline-fill-preserves-declared-longitudinal-material-gap", LongGap);
        check("outline-fill-cancelled-operation-keeps-source-unchanged", Cancelled);
    }

    private static object Threshold(double cell, int smallDepth, int exactDepth, int largeDepth)
    {
        var rectangle = Rectangle();
        var holes = HoleCells();
        bool Small((int R, int C) p) => p.R >= -80 && p.R < -60 && p.C >= 80 - smallDepth;
        bool Exact((int R, int C) p) => p.R >= -20 && p.R < 0 && p.C >= 80 - exactDepth;
        bool Large((int R, int C) p) => p.R >= 40 && p.R < 60 && p.C >= 80 - largeDepth;
        var measured = rectangle.Where(p => !holes.Contains(p) && !Small(p) && !Exact(p) && !Large(p)).ToHashSet();
        var expected = rectangle.Where(p => !holes.Contains(p) && !Small(p) && !Exact(p)).ToHashSet();
        var source = Fixture(measured, cell: cell);
        string snapshot = Snapshot(source);
        var repaired = AngleSolidOutlineFiller.Fill(source);
        var actual = Footprint(repaired, source.Reconstructions[0], cell);
        Assert(actual.SetEquals(expected), "10 mm repair did not preserve shallow/exact-threshold notches while filling only the deeper notch.");
        Assert(Snapshot(source) == snapshot, "Selective edge repair changed the source mesh.");
        Assert(repaired.OutlineFill is not null && repaired.OutlineFill.FilledExteriorCells == 20 * largeDepth, "Reported threshold repair area is incorrect.");
        var topology = VerifyMesh(repaired, 6);
        Assert(topology.ClosedSolidCount == 1 && topology.ThroughHoleCount == 2, "Selective edge repair altered internal hole/body topology.");
        return new { minimumRepairDepthMm = 10, cellMm = cell, preservedDepthsMm = new[] { smallDepth * cell, exactDepth * cell }, repairedDepthMm = largeDepth * cell, filledCells = 20 * largeDepth, exactInteriorHoleCellsPreserved = holes.Count };
    }

    private static object Positive(string output, double thickness, double cell, double yaw, double pitch, double roll)
    {
        var rectangle = Rectangle();
        var holes = HoleCells();
        var measured = rectangle.Where(p => !holes.Contains(p) && !Notch(p)).ToHashSet();
        var source = Fixture(measured, thickness, cell, yaw, pitch, roll);
        var snapshot = Snapshot(source);
        var filled = AngleSolidOutlineFiller.Fill(source, 0);
        Assert(Snapshot(source) == snapshot, "Outline fill mutated the original mesh/frame.");
        var actual = Footprint(filled, source.Reconstructions[0], cell);
        var expected = rectangle.Except(holes).ToHashSet();
        Assert(actual.SetEquals(expected), "Filled footprint does not equal the known rectangle minus the exact original hole cells.");
        Assert(measured.IsSubsetOf(actual), "Outline fill removed previously reconstructed material.");
        Assert(rectangle.Except(actual).ToHashSet().SetEquals(holes), "An enclosed hole changed its exact footprint, even if the hole count stayed equal.");
        var topology = VerifyMesh(filled, thickness);
        Assert(topology.ClosedSolidCount == 1 && topology.ThroughHoleCount == 2, "Straightening changed the independent fixture's closed-body or two-hole topology.");
        Assert(filled.OutlineFill is { EstimatedGeometry: true, OriginalMeasuredPointCloudUnchanged: true }, "Inferred fill is missing provenance metadata.");
        var report = filled.OutlineFill ?? throw new InvalidOperationException("Missing edge fill report.");
        Assert(report.FilledExteriorCells == expected.Count - measured.Count, "Reported added cells disagree with the known notches.");
        Assert(report.RetainedEnclosedHoleCells == holes.Count, "Reported hole area disagrees with exact known holes.");
        double expectedAddedVolume = (expected.Count - measured.Count) * cell * cell * thickness * Math.Sqrt(2);
        Assert(Math.Abs(topology.VolumeMm3 - source.Validate().VolumeMm3 - expectedAddedVolume) < .001, "Fill volume disagrees with the independently known folded thickness and added area.");
        string directory = Path.Combine(output, $"outline-{cell:G}-{thickness:G}");
        Directory.CreateDirectory(directory);
        SolidGltfWriter.Write(Path.Combine(directory, "model.gltf"), Path.Combine(directory, "model.glb"), filled);
        SolidStepWriter.Write(Path.Combine(directory, "model.step"), filled);
        return new { cellMm = cell, thicknessMm = thickness, topology, addedCells = expected.Count - measured.Count, exactHoleCellsPreserved = holes.Count, originalMeshUnchanged = true };
    }

    private static object NoOp(string output)
    {
        var cells = Rectangle().Except(HoleCells()).ToHashSet();
        var source = Fixture(cells);
        string geometry = GeometrySnapshot(source);
        var result = AngleSolidOutlineFiller.Fill(source, 0);
        Assert(Footprint(result, source.Reconstructions[0], 1).SetEquals(cells), "Already straight outer edges were changed.");
        Assert(result.OutlineFill is { Applied: false, SkipReason: null, FilledExteriorCells: 0 }, "No-op fill reported an applied repair or fabricated additional material.");
        Assert(result.Reconstructions.All(frame => !frame.OuterEdgesFilled), "No-op frame falsely claims filled outer edges.");
        Assert(GeometrySnapshot(result) == geometry, "No-op changed source mesh/frame data rather than preserving an exact copy.");
        Assert(Math.Abs(result.Validate().VolumeMm3 - source.Validate().VolumeMm3) < .001, "No-op changed volume.");
        AssertNoAddedMaterialMetadata(result, Path.Combine(output, "outline-no-op"));
        return new { applied = false, filledCells = 0, exactGeometryPreserved = true, glbClaimsNoAddedMaterial = true };
    }

    private static object SmallIsland(string output)
    {
        var main = Rectangle().Except(HoleCells()).ToHashSet();
        var island = (from r in Enumerable.Range(-80, 8) from c in Enumerable.Range(82, 2) select (R: r, C: c)).ToHashSet();
        var source = Fixture(main.Union(island).ToHashSet(), solid: p => p.C >= 82 ? 1 : 0);
        string before = Snapshot(source);
        var result = AngleSolidOutlineFiller.Fill(source, 0);
        Assert(Footprint(result, source.Reconstructions[0], 1).SetEquals(main), "Small edge fragment enlarged the fixed main-body envelope or removed main-body cells.");
        Assert(result.OutlineFill is { Applied: true, ExcludedSmallIslandCount: 1, FilledExteriorCells: 0 }, "Eligible tiny edge island was not reported as an exclusion-only change.");
        Assert(result.Reconstructions.All(frame => !frame.OuterEdgesFilled), "Excluding a fragment falsely claims that outer-edge material was filled.");
        Assert(result.Validate().ClosedSolidCount == 1, "Eligible isolated edge fragment prevented a single main model.");
        Assert(Snapshot(source) == before, "Island selection mutated the source mesh.");
        AssertNoAddedMaterialMetadata(result, Path.Combine(output, "outline-island-only"));
        return new { excludedIslandCells = island.Count, sourceBodies = 2, finalBodies = 1, originalMeshUnchanged = true };
    }

    private static object NearestOuterEdge()
    {
        var checks = new List<object>();
        foreach (var (name, start, expectedExcluded) in new[]
        {
            ("outside-right-gap4", 84, true), ("outside-left-gap4", -48, true),
            ("outside-right-gap6", 86, false), ("inside-far-from-outer-edges", 26, false),
        })
        {
            var main = Rectangle().Except(HoleCells()).ToHashSet();
            if (name.StartsWith("inside")) main.RemoveWhere(p => p.R >= -90 && p.R < -60 && p.C >= 20 && p.C < 40);
            var island = (from r in Enumerable.Range(-80, 5) from c in Enumerable.Range(start, 4) select (R: r, C: c)).ToHashSet();
            var source = Fixture(main.Union(island).ToHashSet(), solid: p => island.Contains(p) ? 1 : 0);
            string before = Snapshot(source);
            var result = AngleSolidOutlineFiller.Fill(source);
            int excluded = result.OutlineFill!.ExcludedSmallIslandCount;
            Assert(excluded == (expectedExcluded ? 1 : 0), $"Incorrect closest outer-boundary distance for {name}.");
            var expected = expectedExcluded ? main : main.Union(island).ToHashSet();
            Assert(Footprint(result, source.Reconstructions[0], 1).SetEquals(expected), $"Changed unrelated footprint for {name}.");
            Assert(result.Validate().ClosedSolidCount == (expectedExcluded ? 1 : 2), $"Incorrect retained body count for {name}.");
            Assert(Snapshot(source) == before, "Outer-edge proximity selection modified source geometry.");
            checks.Add(new { name, excluded, sourceUnchanged = true });
        }
        return checks;
    }

    private static object LargeIsland()
    {
        var main = Rectangle().Except(HoleCells()).ToHashSet();
        var island = (from r in Enumerable.Range(-80, 30) from c in Enumerable.Range(82, 2) select (R: r, C: c)).ToHashSet();
        var source = Fixture(main.Union(island).ToHashSet(), solid: p => p.C >= 82 ? 1 : 0);
        var result = AngleSolidOutlineFiller.Fill(source, 0);
        Assert(result.Validate().ClosedSolidCount == 2, "A substantial secondary body was silently discarded.");
        Assert(Footprint(result, source.Reconstructions[0], 1).SetEquals(main.Union(island)), "A large secondary body was removed or the space between bodies was bridged.");
        Assert(result.OutlineFill is { ExcludedSmallIslandCount: 0 }, "Large island incorrectly qualified as a tiny-fragment exclusion.");
        return new { sourceBodies = 2, finalBodies = 2, substantialBodyPreserved = true };
    }

    private static object RidgeGap()
    {
        var cells = Rectangle().Where(p => p.C < -3 || p.C >= 3).ToHashSet();
        var source = Fixture(cells, solid: p => p.C < 0 ? 0 : 1);
        var result = AngleSolidOutlineFiller.Fill(source, 0);
        Assert(Footprint(result, source.Reconstructions[0], 1).SetEquals(cells), "Unobserved ridge corridor was supplied with estimated material.");
        Assert(result.Validate().ClosedSolidCount == 2, "Two large independent wings were converted into a joined angle.");
        return new { closedBodies = 2, emptyRidgePreserved = true };
    }

    private static object LongGap()
    {
        var cells = Rectangle().Where(p => p.R < 0 || p.R >= 3).ToHashSet();
        var source = Fixture(cells, solid: p => p.R < 0 ? 0 : 1);
        var result = AngleSolidOutlineFiller.Fill(source, 0);
        Assert(Footprint(result, source.Reconstructions[0], 1).SetEquals(cells), "A declared longitudinal material gap was filled.");
        Assert(result.Validate().ClosedSolidCount == 2, "Separated stock segments were merged by outline infill.");
        return new { closedBodies = 2, nominalGapMm = 3, gapPreserved = true };
    }

    private static object Cancelled()
    {
        var source = Fixture(Rectangle());
        string before = Snapshot(source);
        using var cts = new CancellationTokenSource(); cts.Cancel();
        bool cancelled = false;
        try { AngleSolidOutlineFiller.Fill(source, cancellationToken: cts.Token); }
        catch (OperationCanceledException) { cancelled = true; }
        Assert(cancelled && Snapshot(source) == before, "Cancellation was ignored or changed source geometry.");
        return new { cancelled, sourceMeshUnchanged = true };
    }

    private static HashSet<(int R, int C)> Rectangle() =>
        (from r in Enumerable.Range(-120, 240) from c in Enumerable.Range(-40, 120) select (R: r, C: c)).ToHashSet();
    private static HashSet<(int R, int C)> HoleCells() => Rectangle().Where(p =>
        (p.C + 20) * (p.C + 20) + (p.R - 30) * (p.R - 30) <= 16 ||
        (p.C - 35) * (p.C - 35) + (p.R + 40) * (p.R + 40) <= 25).ToHashSet();
    private static bool Notch((int R, int C) p) =>
        p.C >= 70 && (p.R + 120) % 20 < 12 || p.C < -35 && (p.R + 120) % 30 < 8 ||
        p.R < -113 && p.C >= 50 && p.C < 60;

    private static SolidModel Fixture(HashSet<(int R, int C)> cells, double thickness = 6, double cell = 1,
        double yaw = 35, double pitch = 17, double roll = 31, Func<(int R, int C), int>? solid = null)
    {
        ScanVector Rotate(ScanVector p)
        {
            double a = yaw * Math.PI / 180, b = pitch * Math.PI / 180, c = roll * Math.PI / 180;
            var q = new ScanVector(p.X, Math.Cos(b) * p.Y - Math.Sin(b) * p.Z, Math.Sin(b) * p.Y + Math.Cos(b) * p.Z);
            var r = new ScanVector(Math.Cos(c) * q.X + Math.Sin(c) * q.Z, q.Y, -Math.Sin(c) * q.X + Math.Cos(c) * q.Z);
            return new(Math.Cos(a) * r.X - Math.Sin(a) * r.Y, Math.Sin(a) * r.X + Math.Cos(a) * r.Y, r.Z);
        }
        var origin = new ScanVector(371.3, 451.7, 62.9);
        var cross = Rotate(new(1, 0, 0)); var along = Rotate(new(0, 1, 0)); var up = Rotate(new(0, 0, 1));
        double depth = thickness * Math.Sqrt(2);
        var model = new SolidModel { Options = new(thickness, cell) };
        model.Reconstructions.Add(new(origin, cross, along, up, 1, thickness, depth, 0, 0, 0, 0, 0, cells.Count));
        var nodes = new Dictionary<(int Solid, int R, int C), (int Top, int Bottom)>();
        var groups = new Dictionary<(int Solid, int Kind, int At), int>();
        (int Top, int Bottom) Node(int body, int r, int c)
        {
            if (nodes.TryGetValue((body, r, c), out var n)) return n;
            int index = model.Vertices.Count;
            var p = origin + cross.Scale(c * cell) + along.Scale(r * cell) + up.Scale(-Math.Abs(c * cell));
            model.Vertices.Add(p); model.Vertices.Add(p - up.Scale(depth));
            return nodes[(body, r, c)] = (index, index + 1);
        }
        void Triangle(int a, int b, int c, int body, int kind, int at = 0)
        {
            if (!groups.TryGetValue((body, kind, at), out int group))
            {
                groups[(body, kind, at)] = group = model.Planes.Count;
                var p = model.Vertices[a]; model.Planes.Add(new(p, (model.Vertices[b] - p).Cross(model.Vertices[c] - p).Normalize()));
            }
            model.Triangles.Add(new(a, b, c, group, body));
        }
        foreach (var p in cells.OrderBy(p => p.R).ThenBy(p => p.C))
        {
            int body = solid?.Invoke(p) ?? 0, wing = p.C < 0 ? 0 : 1;
            var a = Node(body, p.R, p.C); var b = Node(body, p.R, p.C + 1);
            var d = Node(body, p.R + 1, p.C); var e = Node(body, p.R + 1, p.C + 1);
            Triangle(a.Top, b.Top, e.Top, body, wing); Triangle(a.Top, e.Top, d.Top, body, wing);
            Triangle(a.Bottom, e.Bottom, b.Bottom, body, wing + 2); Triangle(a.Bottom, d.Bottom, e.Bottom, body, wing + 2);
            void Wall((int Top, int Bottom) x, (int Top, int Bottom) y, int kind, int at)
            { Triangle(x.Top, y.Bottom, y.Top, body, kind, at); Triangle(x.Top, x.Bottom, y.Bottom, body, kind, at); }
            if (!cells.Contains((p.R - 1, p.C))) Wall(a, b, 4, p.R);
            if (!cells.Contains((p.R, p.C + 1))) Wall(b, e, 5, p.C + 1);
            if (!cells.Contains((p.R + 1, p.C))) Wall(e, d, 6, p.R + 1);
            if (!cells.Contains((p.R, p.C - 1))) Wall(d, a, 7, p.C);
        }
        model.Validate();
        return model;
    }

    private static HashSet<(int R, int C)> Footprint(SolidModel model, SolidReconstruction frame, double cell)
    {
        var result = new Dictionary<(int R, int C), int>();
        foreach (var t in model.Triangles.Where(t => (t.A & 1) == 0 && (t.B & 1) == 0 && (t.C & 1) == 0))
        {
            var center = (model.Vertices[t.A] + model.Vertices[t.B] + model.Vertices[t.C]).Scale(1d / 3) - frame.Origin;
            var key = (R: (int)Math.Floor(center.Dot(frame.LongAxis) / cell + 1e-7), C: (int)Math.Floor(center.Dot(frame.CrossAxis) / cell + 1e-7));
            result[key] = result.GetValueOrDefault(key) + 1;
        }
        Assert(result.Values.All(n => n == 2), "Unexpected duplicate or missing top triangles in a grid cell.");
        return result.Keys.ToHashSet();
    }
    private static SolidTopology VerifyMesh(SolidModel model, double thickness)
    {
        var edges = new Dictionary<(int, int), List<(int, int)>>();
        foreach (var t in model.Triangles)
        foreach (var e in new[] { (t.A, t.B), (t.B, t.C), (t.C, t.A) })
        {
            var key = e.Item1 < e.Item2 ? e : (e.Item2, e.Item1);
            if (!edges.TryGetValue(key, out var uses)) edges[key] = uses = [];
            uses.Add(e);
        }
        Assert(edges.Values.All(e => e.Count == 2 && e[0].Item1 == e[1].Item2 && e[0].Item2 == e[1].Item1), "Filled model has an open or inconsistently wound mesh edge.");
        double maxError = 0;
        foreach (var t in model.Triangles.Where(t => (t.A & 1) == 0 && (t.B & 1) == 0 && (t.C & 1) == 0))
        {
            var n = (model.Vertices[t.B] - model.Vertices[t.A]).Cross(model.Vertices[t.C] - model.Vertices[t.A]).Normalize();
            foreach (int index in new[] { t.A, t.B, t.C }) maxError = Math.Max(maxError, Math.Abs((model.Vertices[index + 1] - model.Vertices[index]).Dot(n) + thickness));
        }
        Assert(maxError < .001, "Actual face-normal thickness changed during outline fill.");
        return model.Validate();
    }
    private static void AssertNoAddedMaterialMetadata(SolidModel model, string directory)
    {
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "model.glb");
        SolidGltfWriter.Write(Path.Combine(directory, "model.gltf"), path, model);
        byte[] bytes = File.ReadAllBytes(path);
        using var json = JsonDocument.Parse(bytes.AsMemory(20, BitConverter.ToInt32(bytes, 12)));
        var extras = json.RootElement.GetProperty("extras");
        Assert(!extras.GetProperty("outerEdgesFilled").GetBoolean() && !extras.GetProperty("addedOuterGeometryIsEstimated").GetBoolean(),
            "No added cells were produced, but the actual GLB advertises estimated outer-edge infill.");
        Assert(extras.GetProperty("edgeInfillReport").GetProperty("applied").GetBoolean() == model.OutlineFill!.Applied,
            "GLB lost the distinction between a no-op and fragment exclusion.");
    }
    private static string GeometrySnapshot(SolidModel source) => JsonSerializer.Serialize(new { source.Vertices, source.Triangles, source.Planes, source.Reconstructions, source.Options });
    private static string Snapshot(SolidModel source) => JsonSerializer.Serialize(new { source.Vertices, source.Triangles, source.Planes, source.Reconstructions, source.Options, source.OutlineFill });
    private static void Assert(bool passed, string message) { if (!passed) throw new InvalidOperationException(message); }
}

