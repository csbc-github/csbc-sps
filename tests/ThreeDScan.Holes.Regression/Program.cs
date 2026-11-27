using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using ThreeDScan.Infrastructure.Export;
using ThreeDScan.Infrastructure.Export.Models;

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
string? referenceRoot = null, output = null;
bool onlyReplay = false;
var replays = new List<string>();
var scopeReplays = new List<string>();
for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--reference-root": referenceRoot = Path.GetFullPath(args[++i]); break;
        case "--output": output = Path.GetFullPath(args[++i]); break;
        case "--replay": replays.Add(Path.GetFullPath(args[++i])); break;
        case "--scope-replay": scopeReplays.Add(Path.GetFullPath(args[++i])); break;
        case "--only-replay": onlyReplay = true; break;
        default: throw new ArgumentException("Usage: [--output NEW_DIRECTORY] [--reference-root DIRECTORY] [--replay INPUT.ply]");
    }
}
string work = output ?? Path.Combine(Path.GetTempPath(), "ThreeDScan.Holes.Regression", Guid.NewGuid().ToString("N"));
if (onlyReplay && replays.Count == 0 && scopeReplays.Count == 0) throw new ArgumentException("--only-replay requires --replay or --scope-replay INPUT.ply.");
if (Directory.Exists(work)) throw new IOException("Choose a new output directory; existing files are never overwritten.");
Directory.CreateDirectory(work);
var checks = new List<object>();
var failures = new List<string>();
var timer = Stopwatch.StartNew();
int numbers = 0, exports = 0;
double maxDelta = 0;
bool success = false;
try
{
    if (!onlyReplay)
    {
    Check("two-complete-measured-openings-and-native-export", () => Analytic(false));
    Check("partial-opening-does-not-invent-closing-rim", () => Analytic(true));
    Check("solid-plane-does-not-become-hole", () =>
    {
        var p = Fixture(false, false); var r = OrganizedHoleDetector.Detect(p);
        Require(r.Holes.Length == 0 && r.PartialHoles.Length == 0, "A fully measured solid plate became an opening.");
        return new { count = r.PointCount };
    });
    Check("roi-retains-original-ply-vertex-provenance", RoiProvenance);
    Check("malformed-source-fails-without-reindexing", BadInput);
    AngleSteelHoleScopeChecks.Run(work, Check);
    if (referenceRoot is not null) CheckGoldens(referenceRoot);
    }
    if (replays.Count > 0 && referenceRoot is null) throw new ArgumentException("--replay parity requires --reference-root.");
    foreach (string replay in replays) Check("reference-parity-" + Path.GetFileName(Path.GetDirectoryName(replay)), () => Replay(replay));
    for (int i = 0; i < scopeReplays.Count; i++)
    {
        int number = i + 1; string path = scopeReplays[i];
        Check($"angle-scope-replay-{number:D2}", () => AngleSteelHoleScopeChecks.Replay(work, path, number));
    }
    success = failures.Count == 0;
    if (!success) throw new AggregateException(failures.Select(f => new Exception(f)));
    Console.WriteLine($"PASS: {checks.Count} hole checks; {exports} exports; {numbers:N0} numbers; maximum delta {maxDelta:G6}.");
    return 0;
}
catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
finally
{
    File.WriteAllText(Path.Combine(work, "regression-summary.json"), JsonSerializer.Serialize(new
    {
        success, elapsedSeconds = timer.Elapsed.TotalSeconds, numericComparisons = numbers,
        exportComparisons = exports, maxAbsoluteDifference = maxDelta, checks,
    }, HoleJson.Options));
    Console.WriteLine("Artifacts: " + work);
}

void Check(string name, Func<object> run)
{
    try { var detail = run(); checks.Add(new { name, passed = true, detail }); Console.WriteLine("PASS " + name); }
    catch (Exception ex) { failures.Add(name + ": " + ex.Message); checks.Add(new { name, passed = false, error = ex.Message }); Console.Error.WriteLine("FAIL " + name + ": " + ex.Message); }
}

object Analytic(bool partial)
{
    var positions = Fixture(partial);
    var r = OrganizedHoleDetector.Detect(positions);
    Require(r.Holes.Length == (partial ? 1 : 2) && r.PartialHoles.Length == (partial ? 1 : 0), "The independently generated circle/slot closure or count changed.");
    VerifyFloatProvenance(r, positions);
    foreach (var mode in Enum.GetValues<HoleExportMode>())
    {
        using var doc = JsonDocument.Parse(HoleExporter.ToJson(r, "analytic.ply", mode, expectedHoleCount: null));
        Require(doc.RootElement.GetProperty("holes").GetArrayLength() == 2, "Export discarded a separate opening.");
        Require(doc.RootElement.GetProperty("expectedHoleCount").ValueKind == JsonValueKind.Null, "An unknown expected count became a promised count.");
        Require(doc.RootElement.GetProperty("closedHoleCount").GetInt32() == r.Holes.Length, "Export counts disagree with measured closure.");
        exports++;
    }
    return new { r.PointCount, closed = r.Holes.Length, partial = r.PartialHoles.Length };
}

object RoiProvenance()
{
    var positions = Fixture(false);
    string path = Path.Combine(work, "roi-source.ply"); WritePly(path, positions);
    var roi = new FeatureRoi(new FeaturePoint(5, 5, -100), new FeaturePoint(115, 75, 100));
    var analysis = HoleAnalysis.AnalyzeFile(path, roi: roi);
    Require(analysis.Detection.Options.MaxFaces == 4, "Hole analysis differs from the standalone four-face default.");
    Require(analysis.ClosedHoleCount == 2 && analysis.PartialHoleCount == 0, "ROI dropped an independently enclosed opening.");
    Require(analysis.AnalysisSourcePointIndices.Length == analysis.PointCount && analysis.SourcePointCount == positions.Length / 3, "ROI provenance cardinality changed.");
    Require(analysis.Features.Any(f => f.MeasuredBoundary.Zip(analysis.Detection.AllOpenings().Single(h => h.Id == f.Id).PerimeterPoints)
        .Any(p => p.First.SourcePointIndex != p.Second.SourcePointIndex)), "Fixture did not exercise a remapped ROI source index.");
    foreach (var p in analysis.Features.SelectMany(f => f.MeasuredBoundary).Concat(analysis.Features.SelectMany(f => f.EdgePairs).SelectMany(e => new[] { e.Point1, e.Point2 })))
        Require(p.XMm == positions[p.SourcePointIndex * 3] && p.YMm == positions[p.SourcePointIndex * 3 + 1] && p.ZMm == positions[p.SourcePointIndex * 3 + 2], "ROI boundary index no longer identifies the original measured vertex.");
    var files = HoleExporter.Save(analysis, Path.Combine(work, "roi-exports"), path, expectedHoleCount: null);
    using var doc = JsonDocument.Parse(File.ReadAllText(files.BoundaryJsonPath));
    Require(doc.RootElement.GetProperty("sourcePointIndexBasis").GetString() == "SourcePlyVertexIndex", "Mapped export does not identify its index basis.");
    foreach (var hole in doc.RootElement.GetProperty("holes").EnumerateArray())
    foreach (var p in hole.GetProperty("boundaryPoints").EnumerateArray())
    {
        int index = p.GetProperty("sourcePointIndex").GetInt32();
        Require(p.GetProperty("xMm").GetDouble() == positions[index * 3]
            && p.GetProperty("yMm").GetDouble() == positions[index * 3 + 1]
            && p.GetProperty("zMm").GetDouble() == positions[index * 3 + 2], "Native exported boundary does not map to its source PLY.");
    }
    bool rejected = false;
    try { HoleExporter.Save(analysis, Path.Combine(work, "roi-exports"), path); } catch (IOException) { rejected = true; }
    Require(rejected, "An existing measured export was silently replaced.");
    return new { analysis.PointCount, analysis.SourcePointCount, analysis.ClosedHoleCount, analysis.PartialHoleCount };
}

object BadInput()
{
    bool rejected = false;
    try { OrganizedHoleDetector.Detect(new float[] { 0, 0, float.NaN, 1, 1, 1 }); } catch (ArgumentException) { rejected = true; }
    Require(rejected, "Nonfinite measurements were silently dropped and source indices changed.");
    return new { rejected };
}

void CheckGoldens(string root)
{
    string folder = Path.Combine(root, "hole-verification");
    using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "manifest.json")));
    foreach (var item in manifest.RootElement.EnumerateArray())
    {
        string name = item.GetProperty("name").GetString()!, format = item.GetProperty("format").GetString()!;
        if (format == "ply") continue; // Historical manifest paths refer to a different machine; use --replay for available recordings.
        var captured = item.Clone();
        Check("reference-golden-" + name, () =>
        {
            string input = Path.Combine(folder, Path.GetFileName(captured.GetProperty("input").GetString()!));
            string expectedPath = Path.Combine(folder, Path.GetFileName(captured.GetProperty("expected").GetString()!));
            using var expected = JsonDocument.Parse(File.ReadAllText(expectedPath));
            using var reader = new BinaryReader(File.OpenRead(input));
            var positions = new double[reader.BaseStream.Length / (format == "f32" ? 4 : 8)];
            for (int i = 0; i < positions.Length; i++) positions[i] = format == "f32" ? reader.ReadSingle() : reader.ReadDouble();
            int? count = captured.TryGetProperty("count", out var c) ? c.GetInt32() : null;
            var options = captured.TryGetProperty("settings", out var s) ? s.Deserialize<HoleDetectionOptions>(HoleJson.Options) : null;
            HoleDetectionResult? result = null; string? error = null;
            try { result = OrganizedHoleDetector.Detect(positions, count, options); } catch (ArgumentException ex) { error = ex.Message; }
            if (expected.RootElement.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.String)
                Require(error == e.GetString(), "The reference rejected input differently: " + error);
            else
            {
                Require(result is not null, "An expected valid recording was rejected: " + error);
                Compare(expected.RootElement.GetProperty("result"), JsonSerializer.SerializeToElement(result, HoleJson.Options), name + ".result");
                foreach (var mode in Enum.GetValues<HoleExportMode>())
                {
                    using var export = JsonDocument.Parse(HoleExporter.ToJson(result!, name + ".ply", mode));
                    Compare(expected.RootElement.GetProperty("exports").GetProperty(mode.ToString().ToLowerInvariant()), export.RootElement, name + "." + mode);
                    exports++;
                }
                VerifyProvenance(result!, positions);
            }
            return new { closed = result?.Holes.Length, partial = result?.PartialHoles.Length, error };
        });
    }
}

object Replay(string path)
{
    string sourceSha256;
    using (var source = File.OpenRead(path)) sourceSha256 = Convert.ToHexStringLower(SHA256.HashData(source));
    var phase = Stopwatch.StartNew();
    var positions = ScannerPlyReader.Read(path);
    double readSeconds = phase.Elapsed.TotalSeconds; phase.Restart();
    var actual = OrganizedHoleDetector.Detect(positions);
    double currentSeconds = phase.Elapsed.TotalSeconds; phase.Restart();
    string dll = Path.Combine(referenceRoot!, "PlyProcessor.Holes.App", "bin", "Debug", "net9.0-windows", "PlyProcessor.Holes.dll");
    var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(dll);
    var detector = assembly.GetType("PlyProcessor.Holes.OrganizedHoleDetector", true)!;
    var method = detector.GetMethods(BindingFlags.Public | BindingFlags.Static).Single(m => m.Name == "Detect" && m.GetParameters()[0].ParameterType == typeof(float[]));
    var reference = method.Invoke(null, new object?[] { positions, null, null })!;
    double referenceSeconds = phase.Elapsed.TotalSeconds; phase.Restart();
    // Full golden fixtures compare every interior display sample above. For a
    // multi-million-point recording, avoid materializing gigabytes of repeated
    // display-sample JSON merely to compare the measured opening outputs.
    var resolver = new DefaultJsonTypeInfoResolver();
    resolver.Modifiers.Add(info =>
    {
        if (info.Type.Name == "HoleFaceView")
            foreach (var property in info.Properties.Where(p => p.Name == "samples"))
                property.ShouldSerialize = (_, _) => false;
    });
    var compactOptions = new JsonSerializerOptions(HoleJson.Options) { TypeInfoResolver = resolver };
    var expected = JsonSerializer.SerializeToElement(reference, reference.GetType(), compactOptions);
    Compare(expected, JsonSerializer.SerializeToElement(actual, compactOptions), Path.GetFileName(path));
    VerifyFloatProvenance(actual, positions);
    var referenceExporter = assembly.GetType("PlyProcessor.Holes.HoleExporter", true)!;
    var referenceMode = assembly.GetType("PlyProcessor.Holes.HoleExportMode", true)!;
    var exportMethod = referenceExporter.GetMethod("ToJson", BindingFlags.Public | BindingFlags.Static)!;
    foreach (var mode in Enum.GetValues<HoleExportMode>())
    {
        string referenceJson = (string)exportMethod.Invoke(null, new object?[]
            { reference, Path.GetFileName(path), Enum.Parse(referenceMode, mode.ToString()), null })!;
        using var referenceExport = JsonDocument.Parse(referenceJson);
        using var actualExport = JsonDocument.Parse(HoleExporter.ToJson(actual, Path.GetFileName(path), mode, expectedHoleCount: null));
        Compare(referenceExport.RootElement, actualExport.RootElement, Path.GetFileName(path) + "." + mode);
        exports++;
    }
    return new { path, sourceSha256, actual.PointCount, closed = actual.Holes.Length, partial = actual.PartialHoles.Length,
        readSeconds, currentSeconds, referenceSeconds, compareSeconds = phase.Elapsed.TotalSeconds,
        compared = "All faces, view bounds, closure, holes, partial openings, diagnostics, source indices and every measured boundary/scan-row endpoint; display-only interior samples excluded for large replay." };
}

void Compare(JsonElement expected, JsonElement actual, string path)
{
    Require(expected.ValueKind == actual.ValueKind, path + ": JSON kind differs.");
    switch (expected.ValueKind)
    {
        case JsonValueKind.Object:
            Require(expected.EnumerateObject().Count() == actual.EnumerateObject().Count(), path + ": property count differs.");
            foreach (var prop in expected.EnumerateObject())
            {
                Require(actual.TryGetProperty(prop.Name, out _), path + "." + prop.Name + ": missing.");
                Compare(prop.Value, actual.GetProperty(prop.Name), path + "." + prop.Name);
            }
            break;
        case JsonValueKind.Array:
            Require(expected.GetArrayLength() == actual.GetArrayLength(), path + ": array length differs.");
            for (int i = 0; i < expected.GetArrayLength(); i++) Compare(expected[i], actual[i], path + $"[{i}]");
            break;
        case JsonValueKind.Number:
            double delta = Math.Abs(expected.GetDouble() - actual.GetDouble()); numbers++; maxDelta = Math.Max(maxDelta, delta);
            bool discrete = path.EndsWith("sourcePointIndex") || path.EndsWith("scanlines") || path.EndsWith("edgePointCount")
                || path.EndsWith("PointCount") || path.EndsWith("gapEvents") || path.EndsWith("components") || path.Contains(".rejected")
                || path.EndsWith("HoleCount") || path.EndsWith("OpeningCount");
            Require(delta <= (discrete ? 0 : 1e-8), path + $": numeric difference {delta:G6}.");
            break;
        default: Require(expected.ToString() == actual.ToString(), path + ": value differs."); break;
    }
}

void VerifyFloatProvenance(HoleDetectionResult result, float[] positions) => VerifyProvenance(result, Array.ConvertAll(positions, x => (double)x));
void VerifyProvenance(HoleDetectionResult result, double[] positions)
{
    foreach (var h in result.AllOpenings())
    {
        Require(h.Status != "CLOSED" || h.Closure.Lower && h.Closure.Upper, "An unobserved closing rim became complete.");
        foreach (var p in h.PerimeterPoints.Concat(h.EdgePoints.SelectMany(e => new[] { e.Point1, e.Point2 })))
            Require(p.SourcePointIndex >= 0 && p.SourcePointIndex < result.PointCount
                && p.XMm == positions[p.SourcePointIndex * 3] && p.YMm == positions[p.SourcePointIndex * 3 + 1]
                && p.ZMm == positions[p.SourcePointIndex * 3 + 2], "Measured boundary provenance changed.");
    }
}

static float[] Fixture(bool partial, bool openings = true)
{
    var xyz = new List<float>();
    for (int row = partial ? 50 : 0; row <= 160; row++)
    for (int column = 0; column <= 240; column++)
    {
        double x = column * .5, y = row * .5;
        bool circle = Math.Pow(x - 30, 2) + Math.Pow(y - 25, 2) < 36;
        bool slot = Math.Pow(Math.Max(0, Math.Abs(x - 82) - 8), 2) + Math.Pow(y - 50, 2) < 16;
        if (openings && (circle || slot)) continue;
        xyz.Add((float)x); xyz.Add((float)y); xyz.Add((float)(20 + x * .1 + y * .05));
    }
    return xyz.ToArray();
}

static void WritePly(string path, float[] xyz)
{
    using var writer = new StreamWriter(path);
    writer.Write($"ply\nformat ascii 1.0\nelement vertex {xyz.Length / 3}\nproperty float x\nproperty float y\nproperty float z\nend_header\n");
    for (int i = 0; i < xyz.Length; i += 3) writer.WriteLine($"{xyz[i]:R} {xyz[i + 1]:R} {xyz[i + 2]:R}");
}

static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
