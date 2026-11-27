using System.Globalization;
using System.Text.Json;
using ThreeDScan.Infrastructure.Export;
using ThreeDScan.Infrastructure.Export.Models;

/// <summary>Labelled scanner-grid scenes independently identify workpiece and background measurements.</summary>
internal static class AngleSteelHoleScopeChecks
{
    internal static void Run(string work, Action<string, Func<object>> check)
    {
        check("scope-two-wing-holes-above-raised-support", () => Positive(work, "scope-roof", Build()));
        check("scope-rotated-tilted-negative-Z-workpiece", () => Positive(work, "scope-tilted", Build(yaw: 37, tilted: true), requirePartial: false));
        check("scope-explicit-roi-retains-full-source-wing-evidence", () => Positive(work, "scope-roi", Build(),
            new FeatureRoi(new(60, 70, -300), new(115, 130, 300)), requirePartial: false));
        check("scope-single-observed-wing-does-not-use-board-as-second-wing", () => Negative(work, "scope-single", Build(singleWing: true)));
        check("scope-floor-and-support-openings-are-not-angle-holes", () => Negative(work, "scope-no-angle", Build(noWorkpiece: true)));
        check("scope-isolated-flat-plate-is-not-validated-angle", () => Negative(work, "scope-flat-plate", SupportOnly()));
    }

    private static object Positive(string work, string name, Scene scene, FeatureRoi? roi = null, bool requirePartial = true)
    {
        string path = Path.Combine(work, name + ".ply");
        WritePly(path, scene.Positions);
        var unscoped = HoleAnalysis.AnalyzeFile(path, roi: roi);
        var expected = unscoped.Features.Where(f => f.MeasuredBoundary.Length > 0
            && f.MeasuredBoundary.All(p => scene.Labels[p.SourcePointIndex] == 2)).ToArray();
        Require(expected.Count(f => f.Closed) >= (roi is null ? 2 : 1),
            "The independently cut workpiece holes were not resolved by the unscoped detector.");
        if (requirePartial) Require(expected.Any(f => !f.Closed), "The fixture did not exercise an incomplete observed wing opening.");
        if (roi is null) Require(unscoped.Features.Length > expected.Length,
            "The fixture did not exercise a separately measured background opening.");
        var scoped = HoleAnalysis.AnalyzeFile(path, roi: roi, angleSteelOnly: true);
        Require(scoped.Scope is { WorkpieceIdentified: true }, "The workpiece selection was not independently validated.");
        VerifySubset(scoped, unscoped, expected, scene.Positions);
        Require(scoped.Features.SelectMany(f => f.MeasuredBoundary).All(p => scene.Labels[p.SourcePointIndex] == 2),
            "A recorded angle-hole boundary contains a labelled floor or support measurement.");
        VerifyExports(scoped, path, Path.Combine(work, name + "-exports"), scene.Positions, expected.Select(f => f.Id).ToHashSet());
        return new { unscopedCount = unscoped.Features.Length, scopedCount = scoped.Features.Length,
            closed = scoped.ClosedHoleCount, partial = scoped.PartialHoleCount,
            originalVertexCount = scene.Positions.Length / 3, retainedIds = scoped.Features.Select(f => f.Id).ToArray() };
    }

    private static object Negative(string work, string name, Scene scene)
    {
        string path = Path.Combine(work, name + ".ply");
        WritePly(path, scene.Positions);
        var unscoped = HoleAnalysis.AnalyzeFile(path);
        Require(unscoped.Features.Length > 0, "The negative fixture must contain measured openings for the general detector.");
        var scoped = HoleAnalysis.AnalyzeFile(path, angleSteelOnly: true);
        Require(scoped.Features.Length == 0 && scoped.Detection.Holes.Length == 0 && scoped.Detection.PartialHoles.Length == 0,
            "A floor, support or unverified single wing was recorded as a validated angle-steel hole.");
        Require(scoped.FeatureBoundary.Length == 0 && scoped.SelectedFeatureId is null,
            "Unavailable angle scope retained a stale selected hole boundary.");
        Require(scoped.Scope is { WorkpieceIdentified: false } && !string.IsNullOrWhiteSpace(scoped.Scope.Reason),
            "Unavailable angle scope did not report why no validated workpiece was identified.");
        VerifyExports(scoped, path, Path.Combine(work, name + "-exports"), scene.Positions, []);
        return new { unscopedCount = unscoped.Features.Length, scopedCount = scoped.Features.Length, scoped.Warnings };
    }

    internal static object Replay(string work, string path, int number)
    {
        var source = ScannerPlyReader.Read(path);
        var unscoped = HoleAnalysis.AnalyzeFile(path);
        var scoped = HoleAnalysis.AnalyzeFile(path, angleSteelOnly: true);
        Require(scoped.ClosedHoleCount == 5 && scoped.PartialHoleCount == 1,
            "The recorded angle task must retain its five enclosed slots and one partial wing opening.");
        // This is a captured-scene expectation, never a production rule by face ID or count.
        var expected = unscoped.Features.Where(f => f.FaceId == "F03").ToArray();
        Require(expected.Length == 6, "The recorded fixture no longer represents the audited F03 workpiece measurements.");
        VerifySubset(scoped, unscoped, expected, source);
        VerifyExports(scoped, path, Path.Combine(work, $"scope-replay-{number:D2}-exports"), source,
            expected.Select(f => f.Id).ToHashSet());
        return new { path, unscopedCount = unscoped.Features.Length, scopedCount = scoped.Features.Length,
            closed = scoped.ClosedHoleCount, partial = scoped.PartialHoleCount,
            retainedIds = scoped.Features.Select(f => f.Id).ToArray() };
    }

    private static void VerifySubset(HoleAnalysisResult scoped, HoleAnalysisResult original, FeatureOpening[] expected, float[] positions)
    {
        Require(scoped.Features.Select(f => f.Id).ToHashSet().SetEquals(expected.Select(f => f.Id)),
            "Angle scoping discarded a measured wing opening or retained a background opening.");
        Require(scoped.SourcePointCount == original.SourcePointCount && scoped.PointCount == original.PointCount,
            "Scoping re-cropped the detector input and changed gap closure evidence.");
        Require(scoped.AnalysisSourcePointIndices.SequenceEqual(original.AnalysisSourcePointIndices),
            "Scoping changed the detector-to-source vertex map.");
        foreach (var feature in scoped.Features)
        {
            var before = expected.Single(f => f.Id == feature.Id);
            Require(feature.Status == before.Status && feature.Closed == before.Closed && feature.Reason == before.Reason
                && feature.Type == before.Type && feature.WidthMm == before.WidthMm && feature.HeightMm == before.HeightMm,
                "Scoping modified an observed hole's classification, closure or measured bounds.");
            Require(feature.MeasuredBoundary.Length == before.MeasuredBoundary.Length, "Scoping altered the measured perimeter length.");
            for (int i = 0; i < feature.MeasuredBoundary.Length; i++)
            {
                var a = feature.MeasuredBoundary[i]; var b = before.MeasuredBoundary[i];
                Require(a.SourcePointIndex == b.SourcePointIndex && a.XMm == b.XMm && a.YMm == b.YMm && a.ZMm == b.ZMm,
                    "Scoping changed or reindexed a measured perimeter point.");
                VerifyPoint(a, positions);
            }
            Require(feature.EdgePairs.Length == before.EdgePairs.Length, "Scoping altered scan-row edge evidence.");
            foreach (var pair in feature.EdgePairs) { VerifyPoint(pair.Point1, positions); VerifyPoint(pair.Point2, positions); }
        }
        Require(scoped.Detection.AllOpenings().Select(h => h.Id).ToHashSet().SetEquals(expected.Select(f => f.Id)),
            "Native exports would still record background openings even though the API was filtered.");
    }

    private static void VerifyExports(HoleAnalysisResult analysis, string source, string directory, float[] positions, HashSet<string> expected)
    {
        var files = HoleExporter.Save(analysis, directory, source, expectedHoleCount: null);
        foreach (string path in new[] { files.CentersJsonPath, files.EdgesJsonPath, files.BoundaryJsonPath })
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var holes = doc.RootElement.GetProperty("holes").EnumerateArray().ToArray();
            Require(holes.Select(h => h.GetProperty("id").GetString()!).ToHashSet().SetEquals(expected),
                "A native JSON output retained a background opening or lost a measured wing opening.");
            Require(doc.RootElement.GetProperty("closedHoleCount").GetInt32() == analysis.ClosedHoleCount
                && doc.RootElement.GetProperty("partialHoleCount").GetInt32() == analysis.PartialHoleCount,
                "Scoped export counts disagree with the recorded measurement.");
            Require(doc.RootElement.GetProperty("holeScope").GetProperty("mode").GetString() == "AngleSteelOnly",
                "The native export does not identify the validated angle-only recording scope.");
            foreach (var h in holes)
            {
                if (h.TryGetProperty("boundaryPoints", out var boundary)) foreach (var point in boundary.EnumerateArray()) VerifyJsonPoint(point);
                if (h.TryGetProperty("edgePairs", out var pairs)) foreach (var pair in pairs.EnumerateArray())
                { VerifyJsonPoint(pair.GetProperty("point1")); VerifyJsonPoint(pair.GetProperty("point2")); }
            }
        }
        void VerifyJsonPoint(JsonElement p)
        {
            int i = p.GetProperty("sourcePointIndex").GetInt32();
            Require(p.GetProperty("xMm").GetDouble() == positions[i * 3]
                && p.GetProperty("yMm").GetDouble() == positions[i * 3 + 1]
                && p.GetProperty("zMm").GetDouble() == positions[i * 3 + 2],
                "A scoped JSON boundary does not identify its original unmodified source vertex.");
        }
    }

    private static void VerifyPoint(BoundaryPoint p, float[] positions) => Require(
        p.XMm == positions[p.SourcePointIndex * 3] && p.YMm == positions[p.SourcePointIndex * 3 + 1]
        && p.ZMm == positions[p.SourcePointIndex * 3 + 2], "A scoped boundary is not an original measured vertex.");

    private static Scene Build(double yaw = 0, bool tilted = false, bool singleWing = false, bool noWorkpiece = false)
    {
        var positions = new List<float>(); var labels = new List<int>();
        double angle = yaw * Math.PI / 180, c = Math.Cos(angle), s = Math.Sin(angle);
        for (int y = 0; y <= 240; y++)
        for (int x = 0; x <= 240; x++)
        {
            double ground = -100 + (tilted ? .08 * x - .05 * y : 0);
            double z = ground; int label = 0;
            bool support = x is >= 20 and <= 220 && y is >= 20 and <= 220;
            if (support) { z += 20; label = 1; }
            double u = (x - 120) * c + (y - 120) * s, v = -(x - 120) * s + (y - 120) * c;
            bool wing = !noWorkpiece && Math.Abs(u) <= 45 && Math.Abs(v) <= 70 && (!singleWing || u <= 0);
            if (wing)
            {
                z = ground + 70 - Math.Abs(u); label = 2;
                bool circle = (u + 25) * (u + 25) + (v + 30) * (v + 30) < 49;
                bool slot = Math.Pow(Math.Max(0, Math.Abs(v - 25) - 8), 2) + Math.Pow(u - 25, 2) < 25;
                bool partial = (u + 25) * (u + 25) + (v + 68) * (v + 68) < 49;
                if (circle || slot || partial) continue;
            }
            else
            {
                bool backgroundHole = support ? (x - 40) * (x - 40) + (y - 80) * (y - 80) < 64
                    : (x - 10) * (x - 10) + (y - 180) * (y - 180) < 49;
                if (backgroundHole) continue;
            }
            positions.Add(x); positions.Add(y); positions.Add((float)z); labels.Add(label);
        }
        return new(positions.ToArray(), labels.ToArray());
    }

    private static Scene SupportOnly()
    {
        var scene = Build(noWorkpiece: true);
        var points = new List<float>();
        for (int i = 0; i < scene.Labels.Length; i++)
            if (scene.Labels[i] == 1) points.AddRange(scene.Positions.AsSpan(i * 3, 3).ToArray());
        return new(points.ToArray(), Enumerable.Repeat(1, points.Count / 3).ToArray());
    }

    private static void WritePly(string path, float[] xyz)
    {
        using var writer = new StreamWriter(path);
        writer.WriteLine($"ply\nformat ascii 1.0\nelement vertex {xyz.Length / 3}\nproperty float x\nproperty float y\nproperty float z\nend_header");
        for (int i = 0; i < xyz.Length; i += 3)
            writer.WriteLine(FormattableString.Invariant($"{xyz[i]:R} {xyz[i + 1]:R} {xyz[i + 2]:R}"));
    }
    private static void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
    private sealed record Scene(float[] Positions, int[] Labels);
}
