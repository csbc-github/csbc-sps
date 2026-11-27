using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Media.Media3D;
using ThreeDScan.Controls;
using ThreeDScan.Models;

internal static class Program
{
    private static readonly List<object> Results = [];
    private static int _failures;

    [STAThread]
    private static int Main(string[] args)
    {
        string output = Path.GetFullPath(Option(args, "--output") ?? Path.Combine(Path.GetTempPath(), "ThreeDScan-HeadFeatures-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(output);
        string fixtures = Path.Combine(output, "fixtures");
        var first = TaskFixtures.Create(fixtures, "SURFACE-authoritative", twoSegments: true);
        var empty = TaskFixtures.Create(fixtures, "SURFACE-no-heads", heads: false, modelThickness: null);
        var measured = TaskFixtures.Create(fixtures, "SURFACE-measured", measuredLeft: 5.75, measuredRight: 6.125);
        var malformed = TaskFixtures.Create(fixtures, "SURFACE-malformed", malformed: true, modelThickness: null);
        var partial = TaskFixtures.Create(fixtures, "SURFACE-partial");
        var partialJson = JsonNode.Parse(File.ReadAllText(Path.Combine(partial.Root, "task.json")))!;
        partialJson["segments"]![0]!["response"]!["headFeatures"]!["leftCorner"]!.AsObject().Remove("z");
        File.WriteAllText(Path.Combine(partial.Root, "task.json"), partialJson.ToJsonString());
        var wrongId = TaskFixtures.Create(fixtures, "SURFACE-wrong-id");
        var wrongIdJson = JsonNode.Parse(File.ReadAllText(Path.Combine(wrongId.Root, "task.json")))!;
        wrongIdJson["taskId"] = "SURFACE-different";
        File.WriteAllText(Path.Combine(wrongId.Root, "task.json"), wrongIdJson.ToJsonString());
        var hashes = Directory.GetFiles(fixtures, "*", SearchOption.AllDirectories).ToDictionary(p => p, Hash);

        Check("GLB and PLY use the identical authoritative first-segment triplet", () =>
        {
            var glb = HeadFeatureDisplayInfo.ReadTaskMetadataForArtifact(first.Glb);
            var ply = HeadFeatureDisplayInfo.ReadTaskMetadataForArtifact(first.Ply);
            Assert(glb.IsApiTask && glb.HasHeadPoints && glb.SourceTaskId == first.TaskId, "API provenance missing");
            ExpectedPoints(glb);
            SamePoints(glb, ply);
            Near(glb.RidgeZDelta, -6.4);
            return new { glb.SourceTaskId, glb.HasHeadPoints, glb.RidgeZDelta };
        });
        Check("Two-segment caption identifies the first measured front", () =>
        {
            var data = HeadFeatureDisplayInfo.ReadTaskMetadataForArtifact(first.Glb);
            Assert(data.SegmentCount == 2, "Segment count lost");
            Assert(data.SourceLabel == "第1段前緣／共2段／依相機位姿合併", "Wrong caption: " + data.SourceLabel);
            Near(data.LeftCorner!.X, 11.25);
            Near(data.RidgeZDelta, -6.4);
            return new { data.SourceLabel, data.SegmentCount };
        });
        Check("Manual 6 mm remains distinct from unavailable left/right measurements", () =>
        {
            var data = HeadFeatureDisplayInfo.ReadTaskMetadataForArtifact(first.Glb);
            var info = new HeadFeatureDisplayInfo();
            info.ApplyArtifactData(data);
            Near(info.ModelThicknessMm, 6);
            Assert(info.ThicknessLeft is null && info.ThicknessRight is null, "Manual thickness became a measurement");
            return new { info.ModelThicknessMm, info.ThicknessLeft, info.ThicknessRight };
        });
        Check("Actual left/right thickness measurements remain independent", () =>
        {
            var info = new HeadFeatureDisplayInfo();
            info.ApplyArtifactData(HeadFeatureDisplayInfo.ReadTaskMetadataForArtifact(measured.Ply));
            Near(info.ModelThicknessMm, 6);
            Near(info.ThicknessLeft, 5.75);
            Near(info.ThicknessRight, 6.125);
            return new { info.ModelThicknessMm, info.ThicknessLeft, info.ThicknessRight };
        });
        Check("AABB-only API cloud has dimensions but no zero head annotations", () =>
        {
            var data = HeadFeatureDisplayInfo.ReadArtifact(empty.Ply);
            var info = new HeadFeatureDisplayInfo();
            info.ApplyArtifactData(data);
            Assert(info.HasData && !info.HasHeadPoints, "Bounds confused with measured head points");
            Near(info.SizeX, 40); Near(info.SizeY, 100); Near(info.SizeZ, 20);
            Assert(!HeadViewCamera.TryReadPoints(info, out _, out _, out _), "Default zero points became camera markers");
            return new { info.HasData, info.HasHeadPoints, info.SizeX, info.SizeY, info.SizeZ };
        });
        Check("Switching to an empty task clears old points, measurements, model input and bounds", () =>
        {
            var info = new HeadFeatureDisplayInfo();
            info.ApplyArtifactData(HeadFeatureDisplayInfo.ReadArtifact(measured.Ply));
            Assert(info.HasHeadPoints && info.ThicknessLeft is not null && info.SizeX is not null, "Setup did not populate data");
            info.ApplyArtifactData(HeadFeatureDisplayInfo.ReadTaskMetadataForArtifact(empty.Glb));
            Assert(!info.HasData && !info.HasHeadPoints, "Stale head data survived task switch");
            Assert(info.ThicknessLeft is null && info.ThicknessRight is null && info.ModelThicknessMm is null && info.RidgeZDelta is null, "Stale numeric fields survived task switch");
            Assert(info.SizeX is null && info.SizeY is null && info.SizeZ is null, "Stale bounds survived task switch");
            Assert(!HeadViewCamera.TryReadPoints(info, out _, out _, out _), "Camera still uses former task's points");
            return new { info.HasData, info.HasHeadPoints };
        });
        Check("Malformed API task never falls back to a guessed legacy head", () =>
        {
            var data = HeadFeatureDisplayInfo.ReadArtifact(malformed.Glb);
            Assert(data.IsApiTask && !data.HasHeadPoints, "Malformed task used a point-cloud guess");
            Near(data.SizeX, 40);
            return new { data.IsApiTask, data.HasHeadPoints, data.SizeX };
        });
        Check("A partial saved coordinate does not become a synthetic zero point", () =>
        {
            var data = HeadFeatureDisplayInfo.ReadArtifact(partial.Ply);
            Assert(data.IsApiTask && !data.HasHeadPoints, "Partial XYZ accepted as a measured triplet");
            Assert(data.LeftCorner is null && data.Apex is null && data.RightCorner is null, "Partial authoritative data leaked as a complete head");
            return new { data.HasHeadPoints };
        });
        Check("Mismatched task identity does not load another task's measurements", () =>
        {
            var data = HeadFeatureDisplayInfo.ReadArtifact(wrongId.Ply);
            Assert(data.IsApiTask && !data.HasHeadPoints && data.ModelThicknessMm is null, "Wrong task ID accepted");
            return new { data.SourceTaskId, data.HasHeadPoints };
        });
        Check("Cancellation stops artifact reading without publishing partial data", () =>
        {
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            bool cancelled = false;
            try { _ = HeadFeatureDisplayInfo.ReadArtifact(first.Ply, cancellation.Token); }
            catch (OperationCanceledException) { cancelled = true; }
            Assert(cancelled, "Cancelled read continued");
            return new { cancelled };
        });
        Check("Head camera accepts measured finite points and rejects nonfinite coordinates", () =>
        {
            var info = new HeadFeatureDisplayInfo();
            info.ApplyArtifactData(HeadFeatureDisplayInfo.ReadTaskMetadataForArtifact(first.Ply));
            Assert(HeadViewCamera.TryReadPoints(info, out var left, out _, out _), "Valid measured points rejected");
            Near(left.X, 11.25);
            info.Apex.X = double.NaN;
            Assert(!HeadViewCamera.TryReadPoints(info, out _, out _, out _), "Nonfinite annotation accepted");
            return new { finiteAccepted = true, nonfiniteRejected = true };
        });
        Check("Clearing head data disables camera points before the first change notification", () =>
        {
            var info = new HeadFeatureDisplayInfo();
            info.ApplyArtifactData(HeadFeatureDisplayInfo.ReadTaskMetadataForArtifact(first.Ply));
            string? firstProperty = null;
            bool readableAtFirstNotification = true;
            info.PropertyChanged += (sender, change) =>
            {
                if (firstProperty is not null) return;
                firstProperty = change.PropertyName;
                readableAtFirstNotification = HeadViewCamera.TryReadPoints(info, out _, out _, out _);
            };
            info.Clear();
            Assert(firstProperty == nameof(HeadFeatureDisplayInfo.HasHeadPoints) && !readableAtFirstNotification, "An observer can use old head points while the preview changes");
            return new { firstProperty, readableAtFirstNotification };
        });
        Check("A measured point at the origin remains valid when the triplet is explicitly present", () =>
        {
            var info = new HeadFeatureDisplayInfo();
            info.ApplyArtifactData(new ArtifactFeatureData
            {
                HasHeadPoints = true, LeftCorner = new(0, 0, 0), Apex = new(2, 3, 4), RightCorner = new(5, 1, 2),
            });
            Assert(HeadViewCamera.TryReadPoints(info, out var left, out var apex, out var right), "A legitimate zero coordinate was treated as missing");
            Assert(left == new Point3D(0, 0, 0) && apex != right, "Measured triplet changed");
            return new { originAccepted = true };
        });
        Check("Exactly tagged GLB restores scanner millimetres, normals and winding", () => GlbCheck(TinyGlb.ScanConvention, true, false));
        Check("Tagged GLB recomputes bounds from transformed vertices", () => GlbCheck(TinyGlb.ScanConvention, true, true));
        Check("Untagged legacy GLB keeps its original coordinates and normals", () => GlbCheck(null, false, false));
        Check("An unrecognised GLB convention does not trigger an implicit unit conversion", () => GlbCheck("(x,y,z)->(0.001*x,0.001*z,-0.001*y)", false, false));
        Check("All synthetic task and artifact input bytes remain unchanged", () =>
        {
            foreach (var pair in hashes) Assert(Hash(pair.Key) == pair.Value, "Input changed: " + pair.Key);
            return new { files = hashes.Count, unchanged = true };
        });

        string? actualRoot = Option(args, "--task-root");
        if (actualRoot is not null) Check("Saved actual task exposes the same measured heads through GLB and PLY", () => ActualTask(Path.GetFullPath(actualRoot)));
        string? assembly = Option(args, "--wpf-assembly");
        if (assembly is not null) Check("Compiled UI apply guard rejects stale version/path and clears missing data", () => CompiledGuard(Path.GetFullPath(assembly), first, empty));

        string summary = Path.Combine(output, "summary.json");
        File.WriteAllText(summary, JsonSerializer.Serialize(new { checks = Results.Count, failures = _failures, noApplicationOrHardwareStarted = true, results = Results }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"{Results.Count - _failures}/{Results.Count} checks passed; report: {summary}");
        return _failures == 0 ? 0 : 1;
    }

    private static object ActualTask(string root)
    {
        string taskPath = Path.Combine(root, "task.json"), glb = Path.Combine(root, "complete", "stitched.glb"), ply = Path.Combine(root, "complete", "stitched.ply");
        Assert(File.Exists(glb) && File.Exists(ply), "Actual artifacts missing");
        string before = Hash(taskPath);
        using var json = JsonDocument.Parse(File.ReadAllText(taskPath));
        var response = json.RootElement.GetProperty("segments")[0].GetProperty("response");
        var heads = response.GetProperty("headFeatures");
        var a = HeadFeatureDisplayInfo.ReadTaskMetadataForArtifact(glb);
        var b = HeadFeatureDisplayInfo.ReadTaskMetadataForArtifact(ply);
        Assert(a.HasHeadPoints && b.HasHeadPoints && a.SegmentCount == 2, "Actual measured head unavailable");
        SamePoints(a, b);
        CompareJsonPoint(a.LeftCorner!, heads.GetProperty("leftCorner"));
        CompareJsonPoint(a.Apex!, heads.GetProperty("apex"));
        CompareJsonPoint(a.RightCorner!, heads.GetProperty("rightCorner"));
        Near(a.RidgeZDelta, response.GetProperty("segmentZDelta").GetDouble());
        Near(a.ModelThicknessMm, 6);
        Assert(a.ThicknessLeft is null && a.ThicknessRight is null, "Unknown measured thickness replaced by input");
        Assert(Hash(taskPath) == before, "Actual task was modified");
        return new { a.SourceTaskId, a.SourceLabel, a.SegmentCount, a.ModelThicknessMm, a.ThicknessLeft, a.ThicknessRight, a.RidgeZDelta,
            left = new[] { a.LeftCorner!.X, a.LeftCorner.Y, a.LeftCorner.Z }, apex = new[] { a.Apex!.X, a.Apex.Y, a.Apex.Z }, right = new[] { a.RightCorner!.X, a.RightCorner.Y, a.RightCorner.Z },
            taskSha256 = before, metadataOnly = true };
    }

    private static object CompiledGuard(string assemblyPath, ArtifactFixture first, ArtifactFixture empty)
    {
        string directory = Path.GetDirectoryName(assemblyPath)!;
        Assembly? Resolve(AssemblyLoadContext context, AssemblyName name)
        {
            string candidate = Path.Combine(directory, name.Name + ".dll");
            return File.Exists(candidate) ? context.LoadFromAssemblyPath(candidate) : null;
        }
        AssemblyLoadContext.Default.Resolving += Resolve;
        try
        {
            var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(assemblyPath);
            var infoType = assembly.GetType("ThreeDScan.Models.HeadFeatureDisplayInfo", true)!;
            var vmType = assembly.GetType("ThreeDScan.ViewModels.SurfaceScanAreaViewModel", true)!;
            var info = Activator.CreateInstance(infoType)!;
            var vm = RuntimeHelpers.GetUninitializedObject(vmType);
            var reader = infoType.GetMethod("ReadTaskMetadataForArtifact", BindingFlags.Public | BindingFlags.Static)!;
            object good = reader.Invoke(null, [first.Glb])!, missing = reader.Invoke(null, [empty.Glb])!;
            infoType.GetMethod("ApplyArtifactData")!.Invoke(info, [good]);
            const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
            vmType.GetField("_headFeatures", fields)!.SetValue(vm, info);
            vmType.GetField("_glbWatchVersion", fields)!.SetValue(vm, 7);
            vmType.GetField("_lastPlyPath", fields)!.SetValue(vm, empty.Glb);
            var apply = vmType.GetMethod("TryApplyArtifactFeatures", fields)!;
            bool Invoke(object data, int version, string path) => (bool)apply.Invoke(vm, [data, version, path])!;
            bool HasPoints() => (bool)infoType.GetProperty("HasHeadPoints")!.GetValue(info)!;
            Assert(!Invoke(missing, 6, empty.Glb) && HasPoints(), "Old version altered displayed data");
            Assert(!Invoke(missing, 7, first.Glb) && HasPoints(), "Old path altered displayed data");
            Assert(Invoke(missing, 7, empty.Glb) && !HasPoints(), "Current missing-head result did not clear old points");
            Assert(infoType.GetProperty("ModelThicknessMm")!.GetValue(info) is null, "Current task retained old thickness");
            Assert(!Invoke(good, 6, first.Glb) && !HasPoints(), "Late old task repopulated cleared data");
            return new { assemblyPath, constructorsSkipped = true, staleVersionRejected = true, stalePathRejected = true, currentMissingDataCleared = true };
        }
        finally { AssemblyLoadContext.Default.Resolving -= Resolve; }
    }

    private static object GlbCheck(string? convention, bool tagged, bool staleBounds)
    {
        byte[] bytes = TinyGlb.Create(convention, staleBounds), before = bytes.ToArray();
        var mesh = GlbMeshLoader.LoadFromBytes(bytes);
        Assert(bytes.SequenceEqual(before), "GLB bytes changed");
        Point3D[] expected = tagged ? [new(100, 200, 300), new(200, 200, 300), new(100, 300, 300)] : [new(.1, .3, -.2), new(.2, .3, -.2), new(.1, .3, -.3)];
        var normal = tagged ? new Vector3D(0, 0, 1) : new Vector3D(0, 1, 0);
        Assert(mesh.Positions.Count == 3 && mesh.Normals.Count == 3 && mesh.TriangleIndices.SequenceEqual([0, 1, 2]), "Triangle topology changed");
        for (int i = 0; i < 3; i++)
        {
            Assert((mesh.Positions[i] - expected[i]).Length < (tagged ? .0001 : .000001), "Wrong coordinate conversion");
            Assert((mesh.Normals[i] - normal).Length < 1e-10, "Wrong normal conversion");
        }
        var cross = Vector3D.CrossProduct(mesh.Positions[1] - mesh.Positions[0], mesh.Positions[2] - mesh.Positions[0]);
        Assert(Vector3D.DotProduct(cross, normal) > 0, "Triangle winding flipped");
        if (tagged)
        {
            Near(mesh.Bounds.X, 100, .0001); Near(mesh.Bounds.Y, 200, .0001); Near(mesh.Bounds.Z, 300, .0001);
            Near(mesh.Bounds.SizeX, 100, .0001); Near(mesh.Bounds.SizeY, 100, .0001); Near(mesh.Bounds.SizeZ, 0, .0001);
        }
        else
        {
            Near(mesh.Bounds.X, .1); Near(mesh.Bounds.Y, .3); Near(mesh.Bounds.Z, -.3);
            Near(mesh.Bounds.SizeX, .1); Near(mesh.Bounds.SizeY, 0); Near(mesh.Bounds.SizeZ, .1);
        }
        return new { tagged, staleBounds, mesh.Bounds.X, mesh.Bounds.Y, mesh.Bounds.Z, mesh.Bounds.SizeX, mesh.Bounds.SizeY, mesh.Bounds.SizeZ, windingPreserved = true, inputUnchanged = true };
    }

    private static void ExpectedPoints(ArtifactFeatureData data)
    {
        Coord(data.LeftCorner!, 11.25, 22.5, -3.75); Coord(data.Apex!, 44.125, 55.25, 66.5); Coord(data.RightCorner!, 77.75, 88.875, -9.125);
    }
    private static void SamePoints(ArtifactFeatureData a, ArtifactFeatureData b)
    {
        Assert(a.HasHeadPoints && b.HasHeadPoints, "Head triplet unavailable");
        Coord(b.LeftCorner!, a.LeftCorner!.X, a.LeftCorner.Y, a.LeftCorner.Z);
        Coord(b.Apex!, a.Apex!.X, a.Apex.Y, a.Apex.Z);
        Coord(b.RightCorner!, a.RightCorner!.X, a.RightCorner.Y, a.RightCorner.Z);
    }
    private static void CompareJsonPoint(CoordinateInfo point, JsonElement json) => Coord(point, json.GetProperty("x").GetDouble(), json.GetProperty("y").GetDouble(), json.GetProperty("z").GetDouble());
    private static void Coord(CoordinateInfo p, double x, double y, double z) { Near(p.X, x); Near(p.Y, y); Near(p.Z, z); }
    private static void Near(double? actual, double expected, double tolerance = 1e-9) => Assert(actual.HasValue && double.IsFinite(actual.Value) && Math.Abs(actual.Value - expected) <= tolerance, $"Expected {expected:R}, got {actual:R}");
    private static void Assert(bool condition, string reason) { if (!condition) throw new InvalidOperationException(reason); }
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    private static string? Option(string[] args, string name)
    {
        int index = Array.IndexOf(args, name);
        if (index < 0) return null;
        if (index + 1 >= args.Length) throw new ArgumentException("Missing value for " + name);
        return args[index + 1];
    }
    private static void Check(string name, Func<object> test)
    {
        try { var details = test(); Results.Add(new { name, passed = true, details }); Console.WriteLine("PASS " + name); }
        catch (Exception ex) { _failures++; Results.Add(new { name, passed = false, error = ex.ToString() }); Console.WriteLine("FAIL " + name + ": " + ex.GetBaseException().Message); }
    }
}
