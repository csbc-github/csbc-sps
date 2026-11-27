using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using ThreeDScan.Application.Scan.Models;
using ThreeDScan.Domain.Models;
using ThreeDScan.Infrastructure.Export;
using ThreeDScan.Infrastructure.Export.Models;

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
string? output = null, taskFolder = null;
bool onlyReal = false;
for (int i = 0; i < args.Length; i++)
    switch (args[i])
    {
        case "--output" when i + 1 < args.Length: output = Path.GetFullPath(args[++i]); break;
        case "--task-folder" when i + 1 < args.Length: taskFolder = Path.GetFullPath(args[++i]); break;
        case "--only-real": onlyReal = true; break;
        default: throw new ArgumentException("Usage: [--output NEW_DIRECTORY] [--task-folder SURFACE_TASK_DIRECTORY] [--only-real]");
    }
if (onlyReal && taskFolder is null) throw new ArgumentException("--only-real needs --task-folder");
string work = output ?? Path.Combine(Path.GetTempPath(), "ThreeDScan.Stitch.Regression", Guid.NewGuid().ToString("N"));
if (Directory.Exists(work)) throw new IOException("Choose a new output directory.");
Directory.CreateDirectory(work);
var checks = new List<object>();
var failures = new List<string>();
var timer = Stopwatch.StartNew();
StitchValidation? realSeam = null;
bool? realPipelinePassed = null;
if (!onlyReal)
{
    SparseEdgeChecks.Run(Check, (points, indices, bin) => StitchMath.EdgeProfile(points, indices, bin, 0));
    EndCutChecks.Run(Check);
    Check("start-mode-default-and-explicit-compatibility", CheckRequestDefaults);
    Check("legacy-Y-zero-poses-end-to-end", () => Pair("legacy-y", new(0, 0, 0), new(0, 0, 0), 0));
    Check("nearly-scanner-X-end-to-end", () => Pair("near-x", new(89, 0, 0), new(89, 0, 0), 0));
    Check("exact-scanner-X-end-to-end", () => Pair("exact-x", new(90, 0, 0), new(90, 0, 0), 0));
    Check("independent-oblique-XYZ-poses", () => Pair("full-xyz", new(35, 11, 17), new(-58, -9, 23), 0));
    Check("configured-3mm-nominal-gap", () => Pair("gap-3", new(72, 9, 16), new(72, 9, 16), 3));
    Check("three-segments-rigid-chain", CheckThreeSegments);
    Check("zero-gap-joined-model-one-solid-preserves-holes", () => CheckPipelineSolids(0));
    Check("declared-3mm-gap-keeps-two-solid-bodies", () => CheckPipelineSolids(3));
    Check("outline-repair-default-and-opt-out-preserve-measured-PLY", CheckOutlineRepairOptions);
    Check("unmeasured-ridge-corridor-withholds-joined-model", CheckDisconnectedRidge);
    Check("camera-pose-explicitly-preserves-overlap", CheckCameraPose);
    Check("missing-15mm-outer-wing-fails-full-width-quality", CheckMissingWidth);
    Check("one-wing-cannot-fabricate-a-seam", CheckMissingWing);
}
if (taskFolder is not null)
{
    Check("real-eeea-arrangement-and-source-provenance", () => Replay(taskFolder));
    Check("real-eeea-full-width-held-out-quality", () =>
    {
        Require(realSeam is not null && realPipelinePassed.HasValue, "Real replay did not reach seam validation.");
        Require(realSeam!.Passed && realPipelinePassed!.Value,
            $"Actual held-out seam did not pass: RMS {realSeam.CrossSectionHeightRmsMm:F6}, P95 {realSeam.CrossSectionHeightP95AbsoluteMm:F6}, "
            + $"left edge {realSeam.FullSection?.LeftEdgeDistanceMm:F6}, right edge {realSeam.FullSection?.RightEdgeDistanceMm:F6}, "
            + $"coverage {realSeam.FullSection?.CommonCoverageRatio:F6}; "
            + $"A interior edges {realSeam.LeftEndInteriorCoverage?.LeftEdgeDistanceMm:F6}/{realSeam.LeftEndInteriorCoverage?.RightEdgeDistanceMm:F6}, "
            + $"B interior edges {realSeam.RightEndInteriorCoverage?.LeftEdgeDistanceMm:F6}/{realSeam.RightEndInteriorCoverage?.RightEdgeDistanceMm:F6}.");
        return realSeam;
    });
}
File.WriteAllText(Path.Combine(work, "summary.json"), JsonSerializer.Serialize(new
{
    checks = checks.Count, failures = failures.Count, failureMessages = failures,
    elapsedSeconds = timer.Elapsed.TotalSeconds, noHardwareOrDatabaseUsed = true, results = checks,
}, ExtractionJson.Options));
Console.WriteLine($"{(failures.Count == 0 ? "PASS" : "FAIL")}: {checks.Count - failures.Count}/{checks.Count} stitch checks; {work}");
Environment.ExitCode = failures.Count == 0 ? 0 : 1;

void Check(string name, Func<object> action)
{
    try
    {
        var details = action();
        checks.Add(new { name, passed = true, details });
        Console.WriteLine("PASS " + name);
    }
    catch (Exception ex)
    {
        checks.Add(new { name, passed = false, error = ex.Message });
        failures.Add(name + ": " + ex.Message);
        Console.Error.WriteLine("FAIL " + name + ": " + ex.Message);
    }
    GC.Collect();
}

object CheckRequestDefaults()
{
    var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
    var omitted = JsonSerializer.Deserialize<SurfaceScanStartRequest>("{}", json)!;
    Require(omitted.StitchMode == StitchMode.NominalButtJoin, "A new start request omitting mode must join consecutive segments.");
    Require(JsonSerializer.Deserialize<SurfaceScanStartRequest>("{\"stitchMode\":0}", json)!.StitchMode == StitchMode.CameraPose,
        "Explicit numeric 0 must retain CameraPose compatibility.");
    Require(JsonSerializer.Deserialize<SurfaceScanStartRequest>("{\"stitchMode\":1}", json)!.StitchMode == StitchMode.NominalButtJoin,
        "Explicit numeric 1 must mean nominal butt join.");
    Require(SurfaceStartFingerprintRequest.From(omitted, new(), null, null).StitchMode == StitchMode.NominalButtJoin,
        "Idempotency fingerprint lost the effective default mode.");
    Require(JsonSerializer.Deserialize<TaskDocument>("{}", json)!.StitchMode == StitchMode.CameraPose,
        "Existing persisted documents with omitted mode must not silently be reinterpreted.");
    Require(new StitchProcessingOptions().Mode == StitchMode.CameraPose,
        "The low-level explicit options contract was unexpectedly changed.");
    Require(new StitchProcessingOptions().FillOuterEdges && new StitchProcessingOptions().OuterEdgeRepairMinDepthMm == 10,
        "Future model exports must default to repairing only external notches deeper than 10 mm.");
    return new { newStartDefault = omitted.StitchMode.ToString(), oldDocumentDefault = new TaskDocument().StitchMode.ToString() };
}

object Pair(string name, Angles a, Angles b, double gap)
{
    var first = Fixture(220, a, new(315, 420, 70));
    var second = Fixture(245, b, new(420, 515, 115));
    var chain = ChainStitcher.Run(new[] { first.Cloud, second.Cloud }, new ButtStitchOptions(gap));
    var extents = VerifyChain(chain, gap);
    Require(chain.Passed, "Independent planar fixtures did not pass held-out seam checks.");
    Require(extents.TotalLength >= (220 + 245) * .95 + gap && extents.TotalLength <= 220 + 245 + gap + .05,
        $"Joined length {extents.TotalLength:F4} is not the sum of measured lengths plus nominal gap.");
    var forwardA = chain.Seams[0].LeftEnd.Frame.LongAxis;
    var forwardB = chain.Seams[0].RightEnd.Frame.LongAxis;
    Require(Math.Abs(forwardA.Dot(first.LongAxis)) > .999 && Math.Abs(forwardB.Dot(second.LongAxis)) > .999,
        "Detected end direction is not parallel to the independently known analytic workpiece axis.");
    var trueTailA = first.Origin + first.LongAxis.Scale(forwardA.Dot(first.LongAxis) >= 0 ? first.Length : 0);
    var trueHeadB = second.Origin + second.LongAxis.Scale(forwardB.Dot(second.LongAxis) >= 0 ? 0 : second.Length);
    Require(Math.Abs(chain.Parts[0].Pose.Apply(trueTailA).Y
        - chain.Parts[1].Pose.Apply(trueHeadB).Y + gap) < (first.Length + second.Length) * .05,
        "Known analytic tail/head endpoints shifted farther than the bounded end trimming permits.");
    var integration = VerifyPipeline(new[] { first, second }, name, gap, forwardA);
    return new { extents, pipeline = integration };
}

object CheckThreeSegments()
{
    FixtureData[] sources =
    [Fixture(210, new(81, 12, 8), new(350, 450, 80)), Fixture(240, new(-35, -14, 22), new(430, 520, 95)),
     Fixture(260, new(28, 8, -17), new(280, 350, 110))];
    string directory = Path.Combine(work, "three-chain");
    Directory.CreateDirectory(directory);
    _ = SaveFixtures(sources, directory);
    var chain = ChainStitcher.Run(sources.Select(s => s.Cloud).ToArray());
    var extents = VerifyChain(chain, 0);
    File.WriteAllText(Path.Combine(directory, "chain-details.json"), JsonSerializer.Serialize(new
    {
        extents,
        parts = chain.Parts.Select((part, i) => new { i, analyticLength = sources[i].Length, part.RetainedPointCount, part.Pose }),
        seams = chain.Seams.Select(seam => new { seam.SeamNumber, leftOrigin = seam.LeftEnd.Frame.Origin,
            leftAxis = seam.LeftEnd.Frame.LongAxis, rightOrigin = seam.RightEnd.Frame.Origin,
            rightAxis = seam.RightEnd.Frame.LongAxis, seam.Validation }),
    }, ExtractionJson.Options));
    Require(chain.SeamCount == 2 && chain.Passed, "Three inputs must produce two independently validated seams.");
    Require(extents.TotalLength >= 710 * .95 && extents.TotalLength <= 710.05,
        $"Three segments did not extend the total length; actual {extents.TotalLength:F6} from {JsonSerializer.Serialize(extents.Intervals)}.");
    return new { extents, chain.StitchedPointCount };
}

object CheckCameraPose()
{
    var first = Fixture(220, new(89, 8, 17), new(315, 420, 70));
    // Independent files with equal measured samples and identity camera poses.
    var second = Fixture(220, new(89, 8, 17), new(315, 420, 70));
    string directory = Path.Combine(work, "explicit-camera-pose");
    Directory.CreateDirectory(directory);
    var segments = SaveFixtures(new[] { first, second }, directory);
    var result = StitchProcessor.Stitch(segments, Path.Combine(directory, "merged"), options: new() { Mode = StitchMode.CameraPose, ExportModels = false });
    var cloud = ScanPly.Read(result.StitchedPlyPath);
    Require(result.PointCount == first.Cloud.Points.Length * 2, "CameraPose must preserve all overlap samples.");
    var a = cloud.Points.Take(first.Cloud.Points.Length).Select(p => p.Position.Dot(first.LongAxis)).ToArray();
    var b = cloud.Points.Skip(first.Cloud.Points.Length).Select(p => p.Position.Dot(first.LongAxis)).ToArray();
    Require(Math.Abs(a.Min() - b.Min()) < 1e-5 && Math.Abs(a.Max() - b.Max()) < 1e-5,
        "Explicit CameraPose was silently replaced by nominal chaining.");
    return new { result.PointCount, projectedSpan = a.Max() - a.Min(), explicitMode = "CameraPose" };
}

object CheckPipelineSolids(double gap)
{
    FixtureData[] fixtures =
    [Fixture(220, new(35, 17, 31), new(315, 420, 70), hole: true),
     Fixture(245, new(-58, -9, 23), new(420, 515, 115), hole: true)];
    string directory = Path.Combine(work, $"solid-gap-{gap:G}");
    Directory.CreateDirectory(directory);
    var segments = SaveFixtures(fixtures, directory);
    var before = segments.Select(s => Hash(s.ScannerPlyPath)).ToArray();
    var result = StitchProcessor.Stitch(segments, Path.Combine(directory, "joined"), 6, new()
    { Mode = StitchMode.NominalButtJoin, NominalGapMm = gap, ExportModels = true });
    Require(result.ValidationPassed && result.ModelReportPath is not null && result.GlbPath is not null && result.StepPath is not null,
        "A validated independent roof pair did not produce model artifacts.");
    using var json = JsonDocument.Parse(File.ReadAllText(result.ModelReportPath!));
    var topology = json.RootElement.GetProperty("topology");
    int bodies = topology.GetProperty("closedSolidCount").GetInt32();
    int holes = topology.GetProperty("throughHoleCount").GetInt32();
    Require(bodies == (gap == 0 ? 1 : 2),
        $"Nominal gap {gap} produced {bodies} closed solids instead of {(gap == 0 ? 1 : 2)}.");
    Require(holes >= 2, "Joining or separating model bodies filled either measured through-hole.");
    using var emittedGlb = GlbJson(result.GlbPath!);
    Require(segments.Select(s => Hash(s.ScannerPlyPath)).SequenceEqual(before), "Solid construction changed a source point cloud.");
    return new { nominalGapMm = gap, closedSolids = bodies, throughHoles = holes, sourceCloudsUnchanged = true };
}

object CheckDisconnectedRidge()
{
    FixtureData[] fixtures =
    [Fixture(220, new(35, 17, 31), new(315, 420, 70), ridgeVoid: true),
     Fixture(245, new(-58, -9, 23), new(420, 515, 115), ridgeVoid: true)];
    string directory = Path.Combine(work, "unmeasured-ridge");
    Directory.CreateDirectory(directory);
    var segments = SaveFixtures(fixtures, directory);
    var before = segments.Select(s => Hash(s.ScannerPlyPath)).ToArray();
    var result = StitchProcessor.Stitch(segments, Path.Combine(directory, "joined"), 6, new()
    { Mode = StitchMode.NominalButtJoin, NominalGapMm = 0, ExportModels = true });
    Require(result.ValidationPassed, "The independently matching wing ends must reach the model-connectivity gate.");
    Require(result.GlbPath is null && result.StepPath is null && result.ModelReportPath is null,
        "A zero-gap model with an unmeasured longitudinal ridge corridor was falsely published as a connected solid.");
    var measured = ScanPly.Read(result.StitchedPlyPath);
    Require(measured.Points.Length == result.PointCount && result.PointCount > 1000,
        "The unavailable solid model discarded the valid measured stitched cloud.");
    // Direct topology confirms this is the intended two-body footprint, rather
    // than an unrelated export failure. The ridge has no samples to recover.
    var topology = AngleSolidBuilder.Build(measured.Points.Select(p => p.Position), new SolidExportOptions(6)).Validate();
    Require(topology.ClosedSolidCount == 2, "Unmeasured material was added across the empty ridge corridor.");
    Require(segments.Select(s => Hash(s.ScannerPlyPath)).SequenceEqual(before), "The connectivity rejection changed source point clouds.");
    return new { result.PointCount, directClosedSolids = topology.ClosedSolidCount, modelWithheld = true, sourceCloudsUnchanged = true };
}

object CheckOutlineRepairOptions()
{
    FixtureData[] fixtures =
    [Fixture(220, new(35, 17, 31), new(315, 420, 70), hole: true),
     Fixture(245, new(-58, -9, 23), new(420, 515, 115), hole: true)];
    string directory = Path.Combine(work, "outline-repair-options");
    Directory.CreateDirectory(directory);
    var segments = SaveFixtures(fixtures, directory);
    var sourceHashes = segments.Select(s => Hash(s.ScannerPlyPath)).ToArray();
    var enabled = StitchProcessor.Stitch(segments, Path.Combine(directory, "default"), 6,
        new() { Mode = StitchMode.NominalButtJoin });
    var disabled = StitchProcessor.Stitch(segments, Path.Combine(directory, "disabled"), 6,
        new() { Mode = StitchMode.NominalButtJoin, FillOuterEdges = false });
    var cloudOnly = StitchProcessor.Stitch(segments, Path.Combine(directory, "cloud-only"), 6,
        new() { Mode = StitchMode.NominalButtJoin, ExportModels = false });
    Require(enabled.GlbPath is not null && disabled.GlbPath is not null, "Default or explicit legacy outline mode withheld an otherwise valid model.");
    Require(cloudOnly.GlbPath is null && cloudOnly.GltfPath is null && cloudOnly.StepPath is null && cloudOnly.ModelReportPath is null,
        "ExportModels=false generated model artifacts through the outline-repair path.");
    string cloudHash = Hash(enabled.StitchedPlyPath);
    Require(Hash(disabled.StitchedPlyPath) == cloudHash && Hash(cloudOnly.StitchedPlyPath) == cloudHash,
        "Model outline settings changed the actual measured PLY or its source attributes.");
    Require(enabled.PointCount == disabled.PointCount && enabled.ValidationPassed == disabled.ValidationPassed
        && enabled.AlignmentValidationPassed == disabled.AlignmentValidationPassed && enabled.BoundaryValidationPassed == disabled.BoundaryValidationPassed,
        "Model repair changed measured seam quality flags or point counts.");
    using var defaultMetadata = GlbJson(enabled.GlbPath!);
    using var legacyMetadata = GlbJson(disabled.GlbPath!);
    var extras = defaultMetadata.RootElement.GetProperty("extras");
    Require(extras.TryGetProperty("edgeInfillReport", out var fill) && fill.ValueKind == JsonValueKind.Object,
        "Default GLB omits the edge-repair report.");
    Require(fill.GetProperty("minimumNotchDepthMm").GetDouble() == 10,
        "GLB edge-repair metadata lost the configured 10 mm threshold.");
    Require(extras.GetProperty("outerEdgesFilled").GetBoolean() == (fill.GetProperty("filledExteriorCells").GetInt32() > 0),
        "GLB reports completed edge filling when repair was skipped or only excluded a small fragment.");
    var legacyExtras = legacyMetadata.RootElement.GetProperty("extras");
    Require(!legacyExtras.GetProperty("outerEdgesFilled").GetBoolean()
        && (!legacyExtras.TryGetProperty("edgeInfillReport", out var legacyFill) || legacyFill.ValueKind == JsonValueKind.Null),
        "FillOuterEdges=false still applied or advertised outline reconstruction.");
    Require(segments.Select(s => Hash(s.ScannerPlyPath)).SequenceEqual(sourceHashes), "Outline export options modified an input PLY.");
    return new { defaultMinimumDepthMm = 10, optOutRestoresMeasuredOutline = true, measuredPlyByteIdenticalAcrossModes = true, cloudOnlyHasNoModels = true };
}

JsonDocument GlbJson(string path)
{
    byte[] bytes = File.ReadAllBytes(path);
    Require(BitConverter.ToUInt32(bytes, 0) == 0x46546c67 && BitConverter.ToUInt32(bytes, 4) == 2
        && BitConverter.ToUInt32(bytes, 8) == bytes.Length, "Pipeline GLB header or total length is invalid.");
    Require(BitConverter.ToUInt32(bytes, 16) == 0x4e4f534a, "GLB first chunk is not JSON.");
    int jsonLength = BitConverter.ToInt32(bytes, 12), binHeader = 20 + jsonLength;
    Require(jsonLength > 0 && jsonLength % 4 == 0 && binHeader + 8 <= bytes.Length
        && BitConverter.ToUInt32(bytes, binHeader + 4) == 0x004e4942, "GLB lacks an aligned BIN chunk after JSON.");
    int binLength = BitConverter.ToInt32(bytes, binHeader), binStart = binHeader + 8;
    Require(binLength > 0 && binLength % 4 == 0 && binStart + binLength == bytes.Length, "GLB BIN chunk length does not match the file.");
    var document = JsonDocument.Parse(bytes.AsMemory(20, jsonLength));
    try
    {
        var root = document.RootElement;
        var primitive = root.GetProperty("meshes")[0].GetProperty("primitives")[0];
        var attributes = primitive.GetProperty("attributes");
        Require(attributes.TryGetProperty("POSITION", out var positionRef) && attributes.TryGetProperty("NORMAL", out _),
            "glTF attribute semantics must retain case-sensitive POSITION and NORMAL names.");
        var accessors = root.GetProperty("accessors");
        int positionIndex = positionRef.GetInt32(), normalIndex = attributes.GetProperty("NORMAL").GetInt32(), indexIndex = primitive.GetProperty("indices").GetInt32();
        Require(new[] { positionIndex, normalIndex, indexIndex }.All(i => i >= 0 && i < accessors.GetArrayLength()),
            "A primitive references an invalid position, normal or index accessor.");
        var position = accessors[positionIndex]; var normal = accessors[normalIndex]; var indices = accessors[indexIndex];
        int positionCount = position.GetProperty("count").GetInt32(), indexCount = indices.GetProperty("count").GetInt32();
        Require(positionCount > 0 && position.GetProperty("type").GetString() == "VEC3" && position.GetProperty("componentType").GetInt32() == 5126
            && normal.GetProperty("type").GetString() == "VEC3" && normal.GetProperty("componentType").GetInt32() == 5126
            && normal.GetProperty("count").GetInt32() == positionCount, "POSITION/NORMAL accessors must describe corresponding float VEC3 arrays.");
        Require(indices.GetProperty("type").GetString() == "SCALAR" && indices.GetProperty("componentType").GetInt32() == 5125
            && indexCount > 0 && indexCount % 3 == 0 && primitive.GetProperty("mode").GetInt32() == 4,
            "The primitive does not contain uint triangle indices.");
        var views = root.GetProperty("bufferViews");
        int Offset(JsonElement accessor, int elementBytes)
        {
            int viewIndex = accessor.GetProperty("bufferView").GetInt32();
            Require(viewIndex >= 0 && viewIndex < views.GetArrayLength(), "Accessor references an invalid buffer view.");
            var view = views[viewIndex];
            Require(view.GetProperty("buffer").GetInt32() == 0, "Accessor references an unavailable buffer.");
            int start = view.TryGetProperty("byteOffset", out var viewOffset) ? viewOffset.GetInt32() : 0;
            int ownOffset = accessor.TryGetProperty("byteOffset", out var accessorOffset) ? accessorOffset.GetInt32() : 0;
            int length = view.GetProperty("byteLength").GetInt32();
            Require(start >= 0 && ownOffset >= 0 && length >= 0 && (long)start + length <= binLength
                && (long)ownOffset + (long)accessor.GetProperty("count").GetInt32() * elementBytes <= length,
                "Accessor data extends outside its buffer view or BIN chunk.");
            return binStart + start + ownOffset;
        }
        Offset(position, 12); Offset(normal, 12);
        int indexOffset = Offset(indices, 4);
        for (int i = 0; i < indexCount; i++)
            Require(BitConverter.ToUInt32(bytes, indexOffset + i * 4) < positionCount, "A GLB triangle references a vertex outside the POSITION array.");
        return document;
    }
    catch { document.Dispose(); throw; }
}

object CheckMissingWing()
{
    var whole = Fixture(220, new(35, 10, 16), new(315, 420, 70));
    var one = new ScanCloud(whole.Cloud.Header,
        whole.Cloud.Points.Where((_, i) => i % 101 <= 50).Select((p, i) => new SourceScanPoint(i, p.Position, p.ExtraColumns)).ToArray());
    bool rejected = false;
    try { ChainStitcher.Run(new[] { whole.Cloud, one }); }
    catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { rejected = true; }
    Require(rejected, "One plane cannot be supplied with an invented second wing to make a successful seam.");
    return new { rejected };
}

object CheckMissingWidth()
{
    var whole = Fixture(220, new(35, 10, 16), new(315, 420, 70));
    // The independently defined local x range is -50..50. Keep both real
    // planes, but remove the right wing's outer 15 mm from every measured row.
    var partial = new ScanCloud(whole.Cloud.Header,
        whole.Cloud.Points.Where((_, i) => i % 101 <= 85)
            .Select((p, i) => new SourceScanPoint(i, p.Position, p.ExtraColumns)).ToArray());
    var chain = ChainStitcher.Run(new[] { whole.Cloud, partial });
    var quality = chain.Seams[0].Validation;
    Require(!chain.Passed && !quality.Passed && quality.FullSection is { Passed: false },
        "Two real planes with a missing outer 15 mm must not pass by comparing only their common central section.");
    Require(quality.FullSection!.LeftEdgeDistanceMm > 1.5 || quality.FullSection.RightEdgeDistanceMm > 1.5
        || quality.FullSection.CommonCoverageRatio < .98, "The negative width fixture did not exercise the production edge/coverage thresholds.");
    string directory = Path.Combine(work, "missing-width-diagnostics");
    Directory.CreateDirectory(directory);
    // A truncated middle scan must identify both failing seams and retain the
    // measured values through the public processing result and JSON report.
    var sources = new[] { whole, whole with { Cloud = partial }, whole };
    var result = StitchProcessor.Stitch(SaveFixtures(sources, directory), Path.Combine(directory, "joined"), options: new()
    { Mode = StitchMode.NominalButtJoin, ExportModels = false });
    Require(!result.ValidationPassed, "Diagnostics must not turn an incomplete measured wing into a passing stitch.");
    Require(result.AlignmentValidationPassed == true && result.BoundaryValidationPassed == false,
        "The missing-width fixture must distinguish a valid alignment from an unverified boundary without changing strict quality.");
    Require(result.ValidationFailures.Select(f => f.SeamNumber).Distinct().Order().SequenceEqual(new[] { 1, 2 }),
        "The incomplete middle wing must report both failing seams.");
    Require(result.ValidationFailures.Any(f => f.Check.StartsWith("fullSection.") && f.Unit == "mm"
        && f.Comparison == "<=" && f.Limit == 1.5 && f.ActualValue > 10),
        "The missing 15mm edge must report its measured distance and original 1.5mm limit.");
    Require(result.ValidationFailures.All(f => result.Warnings.Contains(f.Message)),
        "Human-readable warnings must identify each failed quality condition.");
    using var report = JsonDocument.Parse(File.ReadAllText(result.ReportPath));
    Require(report.RootElement.GetProperty("result").GetProperty("validationFailures").GetArrayLength() == result.ValidationFailures.Length,
        "The persisted stitch report lost structured failure details.");
    return new { inputWidthMm = 100, observedSecondWidthMm = 85, quality, result.ValidationFailures };
}

object VerifyPipeline(FixtureData[] sources, string name, double gap, ScanVector firstForward)
{
    string directory = Path.Combine(work, name);
    Directory.CreateDirectory(directory);
    var segments = SaveFixtures(sources, directory);
    var result = StitchProcessor.Stitch(segments, Path.Combine(directory, "joined"), options: new()
    { Mode = StitchMode.NominalButtJoin, NominalGapMm = gap, ExportModels = false });
    var report = JsonDocument.Parse(File.ReadAllText(result.ReportPath));
    var matrices = report.RootElement.GetProperty("sources").EnumerateArray()
        .Select(s => s.GetProperty("effectiveScannerToAbsoluteMatrix").EnumerateArray()
            .Select(row => row.EnumerateArray().Select(n => n.GetDouble()).ToArray()).ToArray()).ToArray();
    Require(!result.AbsoluteCoordinatesCalibrated, "Nominal end-to-end arrangement cannot become physically calibrated.");
    Require(result.ValidationPassed, "Known planar pipeline seam validation failed.");
    Require(result.AlignmentValidationPassed == true && result.BoundaryValidationPassed == true,
        "The passing nominal seam must retain both independent quality components.");
    Require(result.ValidationFailures.Length == 0, "Passing measured seams must not report quality failures.");
    var outputCloud = ScanPly.Read(result.StitchedPlyPath);
    VerifyOutputProvenance(outputCloud, sources.Select(s => s.Cloud).ToArray(), matrices);
    var axis = RigidTransforms.Direction(matrices[0], firstForward).Normalize();
    var intervals = GroupIntervals(outputCloud, axis, 3);
    double observedGap = intervals[1].Min - intervals[0].Max;
    Require(observedGap >= gap - 1e-5 && observedGap <= gap + 2.01,
        $"Measured raster envelope gap {observedGap:F6} overlaps or exceeds two analytic 1mm sampling pitches beyond configured {gap}.");
    double totalLength = intervals[^1].Max - intervals[0].Min;
    Require(totalLength > (sources[0].Length + sources[1].Length) * .95, "Pipeline output remained superimposed instead of lengthening.");
    return new { outputPoints = result.PointCount, observedGap, totalLength, result.AbsoluteCoordinatesCalibrated };
}

SegmentProcessingResult[] SaveFixtures(FixtureData[] sources, string directory)
{
    return sources.Select((source, i) =>
    {
        string file = Path.Combine(directory, $"source-{i + 1:D2}.ply");
        ScanPly.Write(file, source.Cloud.Header, source.Cloud.Points.Select(p => (p.Position, p.ExtraColumns)), source.Cloud.Points.Length, "Independent analytic measured roof; no source mutations");
        return new SegmentProcessingResult
        {
            RawPlyPath = file, ScannerPlyPath = file, AbsolutePlyPath = file, ProcessingReportPath = file + ".json",
            SourceSha256 = Hash(file), CameraPosition = new(), ScannerToAbsoluteMatrix = Identity(),
            AbsoluteCoordinatesCalibrated = true, CalibrationId = "analytic-fixture-only", SourcePointIndices = Enumerable.Range(0, source.Cloud.Points.Length).ToArray(),
            Candidates = [], SelectedComponentRank = 1, Warnings = [],
        };
    }).ToArray();
}

object Replay(string folder)
{
    string[] files = Directory.GetDirectories(folder, "segment-*").Order().Select(d => Path.Combine(d, "segment-result.json")).ToArray();
    Require(files.Length == 2, "This recorded replay expects precisely the saved two-segment task.");
    var segments = files.Select(f => JsonSerializer.Deserialize<SegmentProcessingResult>(File.ReadAllText(f), ExtractionJson.Options)!).ToArray();
    var sources = segments.Select(s => ScanPly.Read(s.ScannerPlyPath)).ToArray();
    string[] before = segments.Select(s => Hash(s.ScannerPlyPath)).ToArray();
    var chain = ChainStitcher.Run(sources);
    var extents = VerifyChain(chain, 0, requireSeamPass: false);
    Require(extents.TotalLength > 550 && extents.TotalLength < 650,
        $"Actual 290mm captures should append to roughly 580mm, found {extents.TotalLength:F3}.");
    var result = StitchProcessor.Stitch(segments, Path.Combine(work, "real-eeea"), options: new()
    { Mode = StitchMode.NominalButtJoin, ExportModels = false });
    realSeam = chain.Seams[0].Validation;
    realPipelinePassed = result.ValidationPassed;
    using var report = JsonDocument.Parse(File.ReadAllText(result.ReportPath));
    var matrices = report.RootElement.GetProperty("sources").EnumerateArray()
        .Select(s => s.GetProperty("effectiveScannerToAbsoluteMatrix").EnumerateArray()
            .Select(row => row.EnumerateArray().Select(n => n.GetDouble()).ToArray()).ToArray()).ToArray();
    var outputCloud = ScanPly.Read(result.StitchedPlyPath);
    VerifyOutputProvenance(outputCloud, sources, matrices);
    // The first segment is the sole absolute anchor; measure the joined output
    // along its retained original long direction rather than scanner Y.
    var savedAxis = segments[0].Geometry!.WorkpieceFrame.YAxis;
    var axis = RigidTransforms.Direction(matrices[0], new(savedAxis.X, savedAxis.Y, savedAxis.Z)).Normalize();
    int originalExtraColumns = sources[0].Points[0].ExtraColumns.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
    var intervals = GroupIntervals(outputCloud, axis, originalExtraColumns);
    Require(intervals[1].Min - intervals[0].Max >= -.25, "Actual pipeline sources retain a broad along-axis overlap.");
    Require(!result.AbsoluteCoordinatesCalibrated && !chain.PhysicalOverallLengthVerified,
        "Nominal concatenation was mislabeled as verified physical length or calibration.");
    Require(segments.Select(s => Hash(s.ScannerPlyPath)).SequenceEqual(before), "Replay modified a saved input scan.");
    return new
    {
        taskId = Path.GetFileName(folder), extents, result.PointCount, result.ValidationPassed,
        result.AbsoluteCoordinatesCalibrated, physicalLengthVerified = chain.PhysicalOverallLengthVerified,
        outputIntervalsAlongFirstSavedAxis = intervals, reportPath = result.ReportPath,
        seam = chain.Seams[0].Validation,
    };
}

ChainExtent VerifyChain(ChainStitchResult chain, double gap, bool requireSeamPass = true)
{
    var intervals = chain.Parts.Select(part =>
    {
        var retained = part.TransformedPoints.Where((_, i) => part.Retained[i]).Select(p => p.Y).ToArray();
        Require(retained.Length > 1000, "A segment disappeared during end trimming.");
        for (int i = 0; i < part.SourcePointCount; i++)
            Require((part.Pose.Apply(part.Source.Points[i].Position) - part.TransformedPoints[i]).Length < 2e-6,
                "The composed pose cannot reproduce an original transformed point.");
        return new Extent(retained.Min(), retained.Max());
    }).ToArray();
    double observedGaps = 0;
    for (int i = 1; i < intervals.Length; i++)
    {
        double measuredGap = intervals[i].Min - intervals[i - 1].Max;
        Require(measuredGap >= gap - 1e-5 && measuredGap <= gap + 2.01,
            $"Adjacent measured intervals overlap or exceed two 1mm fixture pitches beyond the nominal cut: {measuredGap:F6}.");
        observedGaps += measuredGap;
    }
    double total = intervals[^1].Max - intervals[0].Min;
    Require(Math.Abs(total - intervals.Sum(i => i.Max - i.Min) - observedGaps) < .05,
        "Output length does not equal retained measured segment lengths plus their observed envelope gaps.");
    Require(!chain.PhysicalOverallLengthVerified, "A geometric nominal join must not verify physical stock length.");
    foreach (var seam in chain.Seams)
    {
        Require(seam.Validation.FitAndValidationIndicesDisjoint, "Training leaked into held-out seam validation.");
        Require(!seam.Validation.AbsoluteLongitudinalPlacementVerified && !seam.Validation.PhysicalOverallLengthVerified,
            "Nominal placement was incorrectly marked physically verified.");
        if (requireSeamPass) Require(seam.Validation.Passed, "Held-out analytic seam validation failed.");
    }
    return new(intervals, total);
}

void VerifyOutputProvenance(ScanCloud outputCloud, ScanCloud[] sources, double[][][] matrices)
{
    var seen = new HashSet<(int, int)>();
    foreach (var point in outputCloud.Points)
    {
        var fields = point.ExtraColumns.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        int scan = int.Parse(fields[^2]), index = int.Parse(fields[^1]);
        Require(scan >= 0 && scan < sources.Length && index >= 0 && index < sources[scan].Points.Length, "Invalid source provenance.");
        Require(seen.Add((scan, index)), "A retained source measurement was duplicated.");
        var original = sources[scan].Points[index];
        Require(string.Join(' ', fields[..^2]) == original.ExtraColumns, "Original intensity/raw index/custom attributes were altered.");
        Require((point.Position - RigidTransforms.Apply(matrices[scan], original.Position)).Length < 2e-6,
            "Output coordinate is not its documented rigid transform of the original measurement.");
    }
    Require(seen.Select(p => p.Item1).Distinct().Count() == sources.Length, "One input segment lost all provenance.");
}

Extent[] GroupIntervals(ScanCloud cloud, ScanVector axis, int originalExtraColumns)
    => cloud.Points.GroupBy(p => int.Parse(p.ExtraColumns.Split(' ', StringSplitOptions.RemoveEmptyEntries)[originalExtraColumns]))
        .OrderBy(g => g.Key).Select(g => new Extent(g.Min(p => p.Position.Dot(axis)), g.Max(p => p.Position.Dot(axis)))).ToArray();

FixtureData Fixture(int length, Angles angles, ScanVector origin, bool hole = false, bool ridgeVoid = false)
{
    double yaw = angles.Yaw * Math.PI / 180, pitch = angles.Pitch * Math.PI / 180, roll = angles.Roll * Math.PI / 180;
    ScanVector Rotate(ScanVector p)
    {
        var a = new ScanVector(p.X, Math.Cos(pitch) * p.Y - Math.Sin(pitch) * p.Z, Math.Sin(pitch) * p.Y + Math.Cos(pitch) * p.Z);
        var b = new ScanVector(Math.Cos(roll) * a.X + Math.Sin(roll) * a.Z, a.Y, -Math.Sin(roll) * a.X + Math.Cos(roll) * a.Z);
        return new(Math.Cos(yaw) * b.X - Math.Sin(yaw) * b.Y, Math.Sin(yaw) * b.X + Math.Cos(yaw) * b.Y, b.Z);
    }
    var points = new List<SourceScanPoint>();
    for (int y = 0; y <= length; y++)
        for (int x = -50; x <= 50; x++)
        {
            if (ridgeVoid && Math.Abs(x) < 3) continue;
            if (hole && (x + 28) * (x + 28) + (y - 100) * (y - 100) <= 49) continue;
            int i = points.Count;
            points.Add(new(i, origin + Rotate(new(x, y, -Math.Abs(x))), $"{i * 13 % 256} {i} {(i % 29) * .25:F2}"));
        }
    string[] header = ["ply", "format ascii 1.0", "element vertex 0", "property float x", "property float y", "property float z",
        "property uchar intensity", "property int raw_point_index", "property float temperature", "end_header"];
    return new(new(header, points.ToArray()), Rotate(new(0, 1, 0)), origin, length);
}
double[][] Identity() => [[1, 0, 0, 0], [0, 1, 0, 0], [0, 0, 1, 0], [0, 0, 0, 1]];
string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
internal sealed record Angles(double Yaw, double Pitch, double Roll);
internal sealed record FixtureData(ScanCloud Cloud, ScanVector LongAxis, ScanVector Origin, double Length);
internal sealed record Extent(double Min, double Max);
internal sealed record ChainExtent(Extent[] Intervals, double TotalLength);
