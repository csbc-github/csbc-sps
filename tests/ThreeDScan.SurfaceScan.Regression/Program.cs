using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using ThreeDScan.Domain.Models;
using ThreeDScan.Infrastructure.Export;
using ThreeDScan.Infrastructure.Export.Models;

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
var replayPaths = new List<string>();
string? requestedOutput = null;
bool keep = false, onlyReplay = false, onlyPlatform = false;
for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--replay" when i + 1 < args.Length: replayPaths.Add(Path.GetFullPath(args[++i])); break;
        case "--output" when i + 1 < args.Length: requestedOutput = Path.GetFullPath(args[++i]); keep = true; break;
        case "--keep": keep = true; break;
        case "--only-replay": onlyReplay = true; break;
        case "--only-platform": onlyPlatform = true; break;
        default: throw new ArgumentException("Usage: [--keep] [--output NEW_DIRECTORY] [--replay INPUT.ply] [--only-replay]");
    }
}

string temporaryParent = Path.Combine(Path.GetTempPath(), "ThreeDScan.SurfaceScan.Regression");
if (onlyReplay && replayPaths.Count == 0) throw new ArgumentException("--only-replay needs at least one --replay INPUT.ply.");
string work = requestedOutput ?? Path.Combine(temporaryParent, Guid.NewGuid().ToString("N"));
if (Directory.Exists(work)) throw new IOException("Choose a new output directory; existing artifacts are never overwritten.");
Directory.CreateDirectory(work);
var checks = new List<object>();
var failures = new List<Exception>();
var timer = Stopwatch.StartNew();
bool success = false;
try
{
    if (!onlyReplay)
    {
        if (!onlyPlatform)
        {
        foreach (double angle in new[] { 0.0, 35.0, 80.0, 90.0, 125.0 })
            RunCheck($"sloped-ground-roof-{angle:F0}", () => CheckRoof(angle));
        RunCheck("flat-top-with-measured-3mm-side", CheckFlatPlate);
        RunCheck("one-visible-wing-does-not-use-ground-as-second-wing", CheckSingleVisibleWing);
        RunCheck("noisy-ground-near-measured-wing-foot", CheckNoisyGround);
        RunCheck("connected-nonwing-support-does-not-extend-geometry", CheckConnectedSupport);
        RunCheck("ground-only-does-not-become-workpiece", CheckGroundOnly);
        RunCheck("perimeter-ground-under-dominant-raised-top", CheckGroundUnderDominantObject);
        RunCheck("cancel-before-extraction", CheckCancellation);
        WingStabilityChecks.Run((name, check) => RunCheck(name, () => checks.Add(check())));
        }
        RunCheck("roof-on-raised-platform", () => CheckPlatformPositive("roof-on-raised-platform", PlatformScenes.Roof()));
        RunCheck("rotated-roof-on-tilted-platform", () => CheckPlatformPositive("rotated-roof-on-tilted-platform", PlatformScenes.Roof(tilted: true)));
        RunCheck("multiple-roofs-on-one-platform", () => CheckPlatformPositive("multiple-roofs-on-one-platform", PlatformScenes.Roof(multiple: true), multiple: true));
        RunCheck("lone-raised-platform-remains-planar", () => CheckPlatformNegative("lone-raised-platform-remains-planar", PlatformScenes.Plate()));
        RunCheck("platform-3mm-side-is-not-wing", () => CheckPlatformNegative("platform-3mm-side-is-not-wing", PlatformScenes.Plate(thinSide: true)));
        RunCheck("platform-cannot-replace-missing-wing", () => CheckPlatformNegative("platform-cannot-replace-missing-wing", PlatformScenes.Roof(singleWing: true)));
        RunCheck("unequal-flat-L-on-platform", () => CheckPlatformPositive("unequal-flat-L-on-platform", PlatformScenes.UnequalFlatL()));
        RunCheck("rotated-flat-L-on-tilted-platform", () => CheckPlatformPositive("rotated-flat-L-on-tilted-platform", PlatformScenes.UnequalFlatL(tilted: true)));
        RunCheck("planar-selection-retains-original-support-cloud", CheckPlatformPlanarSelection);
    }
    for (int i = 0; i < replayPaths.Count; i++)
    {
        int number = i + 1;
        RunCheck($"replay-{number:D2}", () => Replay(replayPaths[number - 1], number));
    }
    if (failures.Count > 0) throw new AggregateException($"{failures.Count} surface-scan check(s) failed.", failures);
    success = true;
    Console.WriteLine($"PASS: {checks.Count} surface-scan checks in {timer.Elapsed.TotalSeconds:F2} s.");
}
catch (Exception ex)
{
    keep = true;
    Console.Error.WriteLine($"FAIL: {ex}");
    Environment.ExitCode = 1;
}
finally
{
    File.WriteAllText(Path.Combine(work, "regression-summary.json"), JsonSerializer.Serialize(new
    {
        success, elapsedSeconds = timer.Elapsed.TotalSeconds, checks,
    }, ExtractionJson.Options));
    if (keep) Console.WriteLine($"Artifacts: {work}");
    else if (requestedOutput is null)
    {
        string full = Path.GetFullPath(work);
        string parent = Path.GetFullPath(temporaryParent) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(parent, StringComparison.OrdinalIgnoreCase)
            || !Guid.TryParseExact(Path.GetFileName(full), "N", out _))
            throw new IOException("Refusing to remove a directory outside this run's GUID-named temporary workspace.");
        Directory.Delete(full, true);
    }
}

void RunCheck(string name, Action check)
{
    try { check(); }
    catch (Exception ex)
    {
        failures.Add(ex);
        checks.Add(new { name, passed = false, error = ex.Message });
        Console.Error.WriteLine($"FAIL {name}: {ex.Message}");
    }
}

void CheckRoof(double angle)
{
    string name = $"sloped-ground-roof-{angle:F0}";
    var scene = Scenes.Roof(angle);
    var result = Process(scene, name);
    Require(result.Geometry is not null, $"{name}: complete observed wings must produce geometry; {result.GeometryError}");
    Require(result.Candidates.Single(c => c.Rank == result.SelectedComponentRank).IsTwoWing,
        $"{name}: a retained floor component must not replace the roof.");
    var selected = result.SourcePointIndices.ToHashSet();
    int floorIncluded = selected.Count(i => scene.Labels[i] == Kind.Ground);
    int lowTotal = Enumerable.Range(0, scene.Labels.Length).Count(i => scene.Labels[i] == Kind.LowWing);
    int lowIncluded = selected.Count(i => scene.Labels[i] == Kind.LowWing);
    double lowRecall = lowIncluded / (double)lowTotal;
    Require(floorIncluded <= Math.Max(3, selected.Count * .005),
        $"{name}: included {floorIncluded} measured ground points out of {selected.Count}.");
    Require(lowRecall >= .90, $"{name}: only {lowRecall:P2} of measured 2.5–10.5 mm wing edges survived the 12 mm seed threshold.");
    Require(Math.Abs(result.Geometry!.WingNormalAngleDegrees - 90) < 2,
        $"{name}: returned angle {result.Geometry.WingNormalAngleDegrees:F3}° is inconsistent with the independent fixture.");
    var axis = new ScanVector(result.Geometry.WorkpieceFrame.YAxis.X,
        result.Geometry.WorkpieceFrame.YAxis.Y, result.Geometry.WorkpieceFrame.YAxis.Z);
    Require(Math.Abs(axis.Dot(scene.ExpectedLongAxis)) > .995,
        $"{name}: inferred long axis must follow the observed rotated roof.");
    VerifyProvenance(scene.Cloud, result);
    checks.Add(new { name, selectedPoints = selected.Count, floorIncluded, lowTotal, lowIncluded, lowRecall,
        wingAngle = result.Geometry.WingNormalAngleDegrees });
    Console.WriteLine($"PASS {name}: low-edge recall {lowRecall:P1}, ground {floorIncluded}/{selected.Count}.");
}

void CheckPlatformPositive(string name, PlatformScene fixture, bool multiple = false)
{
    var scene = fixture.Scene;
    var result = Process(scene, name);
    Require(result.Geometry is not null, $"{name}: visible independent wing pair was lost on the support plane; {result.GeometryError}");
    var selected = result.SourcePointIndices.ToHashSet();
    var selectedCandidate = result.Candidates.Single(c => c.Rank == result.SelectedComponentRank);
    Require(selectedCandidate.ParentComponentRank is int parentRank
        && result.Candidates.Any(c => c.Rank == parentRank && c.ParentComponentRank is null),
        $"{name}: a validated child lost the original parent component lineage.");
    int board = selected.Count(i => scene.Labels[i] == Kind.Platform);
    int ground = selected.Count(i => scene.Labels[i] == Kind.Ground);
    Require(board + ground < selected.Count * .005, $"{name}: support/floor contaminated {board + ground}/{selected.Count} selected points.");
    int objectId = selected.Where(i => fixture.ObjectIds[i] != 0).GroupBy(i => fixture.ObjectIds[i]).MaxBy(g => g.Count())!.Key;
    int otherObject = selected.Count(i => fixture.ObjectIds[i] != 0 && fixture.ObjectIds[i] != objectId);
    Require(otherObject < selected.Count * .005, $"{name}: selected cloud joined two distinct physical objects.");
    int lowTotal = Enumerable.Range(0, scene.Labels.Length).Count(i => fixture.ObjectIds[i] == objectId && scene.Labels[i] == Kind.LowWing);
    int lowIncluded = selected.Count(i => fixture.ObjectIds[i] == objectId && scene.Labels[i] == Kind.LowWing);
    Require(lowIncluded >= lowTotal * .90, $"{name}: recovered only {lowIncluded}/{lowTotal} measured low-edge samples.");
    Require(Math.Abs(result.Geometry!.WingNormalAngleDegrees - 90) < 2, $"{name}: wrong measured wing angle.");
    var axis = result.Geometry.WorkpieceFrame.YAxis;
    Require(Math.Abs(new ScanVector(axis.X, axis.Y, axis.Z).Dot(fixture.LongAxes[objectId])) > .995,
        $"{name}: reported axis does not follow the selected measured workpiece.");
    if (multiple) Require(result.Candidates.Count(c => c.IsTwoWing) >= 2, $"{name}: two independently visible objects were not offered separately.");
    VerifyProvenance(scene.Cloud, result);
    checks.Add(new { name, selectedPoints = selected.Count, platformIncluded = board, groundIncluded = ground,
        otherObjectIncluded = otherObject, lowTotal, lowIncluded, selectedObjectId = objectId,
        twoWingCandidates = result.Candidates.Count(c => c.IsTwoWing), angle = result.Geometry.WingNormalAngleDegrees });
    Console.WriteLine($"PASS {name}: platform {board}, low-edge {lowIncluded}/{lowTotal}, objects {result.Candidates.Count(c => c.IsTwoWing)}.");
}

void CheckPlatformNegative(string name, PlatformScene fixture)
{
    var result = Process(fixture.Scene, name);
    Require(result.PointCount > 1000, $"{name}: visible original planar data must remain available.");
    Require(result.Geometry is null && !result.Candidates.Any(c => c.IsTwoWing), $"{name}: platform or thin side became a fabricated second wing.");
    VerifyProvenance(fixture.Scene.Cloud, result);
    checks.Add(new { name, result.PointCount, result.GeometryError });
    Console.WriteLine($"PASS {name}: measured cloud retained, no fake wing pair.");
}

void CheckPlatformPlanarSelection()
{
    const string name = "planar-selection-retains-original-support-cloud";
    var fixture = PlatformScenes.Roof();
    var result = Process(fixture.Scene, name, new SegmentProcessingOptions { Selection = ComponentSelection.Planar });
    var selected = result.Candidates.Single(c => c.Rank == result.SelectedComponentRank);
    Require(selected.ParentComponentRank is null, "Planar mode substituted a separated child for the original measured component.");
    int totalBoard = fixture.Scene.Labels.Count(k => k == Kind.Platform);
    int retainedBoard = result.SourcePointIndices.Count(i => fixture.Scene.Labels[i] == Kind.Platform);
    Require(retainedBoard >= totalBoard * .95, "Planar mode discarded the original support-plane samples needed by hole analysis.");
    VerifyProvenance(fixture.Scene.Cloud, result);
    checks.Add(new { name, totalBoard, retainedBoard, result.SelectedComponentRank, selected.ParentComponentRank });
    Console.WriteLine($"PASS {name}: retained {retainedBoard}/{totalBoard} measured board samples.");
}

void CheckFlatPlate()
{
    const string name = "flat-top-with-measured-3mm-side";
    var scene = Scenes.Plate();
    var result = Process(scene, name);
    Require(result.PointCount > 1000, "A visible raised planar workpiece should remain available as a measured point cloud.");
    Require(result.Geometry is null && !result.Candidates.Any(c => c.IsTwoWing),
        "A 3 mm plate-thickness face must not be reported as a full second wing of a 90° angle steel.");
    Require(result.SourcePointIndices.Count(i => scene.Labels[i] == Kind.Ground) <= result.PointCount * .005,
        "Raised flat workpiece export absorbed the surrounding ground.");
    VerifyProvenance(scene.Cloud, result);
    checks.Add(new { name, result.PointCount, result.GeometryError });
    Console.WriteLine($"PASS {name}: retained measured planar cloud, no invented two-wing geometry.");
}

void CheckGroundOnly()
{
    const string name = "ground-only-does-not-become-workpiece";
    var scene = Scenes.Ground();
    bool rejected = false;
    try
    {
        var result = Process(scene, name);
        Require(result.Geometry is null, "A single sloped ground plane became angle steel geometry.");
        rejected = result.PointCount == 0;
    }
    catch (InvalidOperationException) { rejected = true; }
    Require(rejected, "A ground-only scan must not export the ground as a raised workpiece.");
    checks.Add(new { name });
    Console.WriteLine($"PASS {name}.");
}

void CheckSingleVisibleWing()
{
    const string name = "one-visible-wing-does-not-use-ground-as-second-wing";
    var scene = Scenes.SingleWing();
    var result = Process(scene, name);
    Require(result.PointCount > 500, "The visible measured wing disappeared from the extracted cloud.");
    Require(result.Geometry is null && !result.Candidates.Any(c => c.IsTwoWing),
        "A missing second wing was substituted with the ground or an extrapolated plane.");
    VerifyProvenance(scene.Cloud, result);
    checks.Add(new { name, result.PointCount, result.GeometryError });
    Console.WriteLine($"PASS {name}.");
}

void CheckNoisyGround()
{
    const string name = "noisy-ground-near-measured-wing-foot";
    var scene = Scenes.NoisyGroundRoof();
    var result = Process(scene, name);
    Require(result.Geometry is not null, $"{name}: the two visible wings must remain measurable.");
    int includedGround = result.SourcePointIndices.Count(i => scene.Labels[i] == Kind.Ground);
    var report = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(result.ProcessingReportPath));
    double groundRms = report.GetProperty("extractionGroundCorrection").GetProperty("groundResidualRmseMm").GetDouble();
    Require(groundRms >= .35, $"The high-noise fixture did not exercise the intended RMS regime ({groundRms:F3} mm).");
    Require(includedGround <= 3,
        $"Recovery grew into {includedGround} noisy table samples near the feet; a capped clearance is below three measured RMS.");
    int lowTotal = scene.Labels.Count(label => label == Kind.LowWing);
    int lowIncluded = result.SourcePointIndices.Count(i => scene.Labels[i] == Kind.LowWing);
    Require(lowIncluded >= lowTotal * .90, "Noise rejection removed clear measured wing-edge samples along with the ground.");
    VerifyProvenance(scene.Cloud, result);
    checks.Add(new { name, groundRmsMm = groundRms, includedGround, lowTotal, lowIncluded });
    Console.WriteLine($"PASS {name}: RMS {groundRms:F3} mm, ground {includedGround}.");
}

void CheckConnectedSupport()
{
    const string name = "connected-nonwing-support-does-not-extend-geometry";
    var scene = Scenes.RoofWithConnectedSupport();
    var result = Process(scene, name);
    var geometry = result.Geometry;
    Require(geometry is not null, $"{name}: independent wing planes were lost among a small connected support.");
    Point3[] ends = [geometry!.HeadFeatures.LeftCorner, geometry.HeadFeatures.Apex, geometry.HeadFeatures.RightCorner,
        geometry.TailFeatures.LeftCorner, geometry.TailFeatures.Apex, geometry.TailFeatures.RightCorner];
    Require(ends.All(point => Math.Abs(point.Y) <= 72),
        $"Non-wing support extended the observed roof ends: Y={string.Join(", ", ends.Select(p => p.Y.ToString("F2")))}.");
    Require(ends.All(point => Math.Abs(point.X) <= 42),
        $"Non-wing support widened fitted roof corners: X={string.Join(", ", ends.Select(p => p.X.ToString("F2")))}.");
    VerifyProvenance(scene.Cloud, result);
    checks.Add(new { name, headY = geometry.HeadFeatures.Apex.Y, tailY = geometry.TailFeatures.Apex.Y,
        selectedSupportSamples = result.SourcePointIndices.Count(i => scene.Labels[i] == Kind.NonWingSupport) });
    Console.WriteLine($"PASS {name}: fitted end stations remain within the measured roof.");
}

void CheckGroundUnderDominantObject()
{
    const string name = "perimeter-ground-under-dominant-raised-top";
    var scene = Scenes.DominantTop();
    var extraction = AngleSteelExtractor.Process(scene.Cloud);
    double[] residuals = scene.Cloud.Points.Where((_, i) => scene.Labels[i] == Kind.Ground)
        .Select(p => Math.Abs(extraction.Calibration.Transform(p.Position).Z)).Order().ToArray();
    double p95 = residuals[(int)(.95 * (residuals.Length - 1))];
    Require(p95 < .35, $"Dominant object top was mistaken for the floor; actual floor p95 residual={p95:F3} mm.");
    int[] rawIndices = extraction.Objects.SelectMany(c => c.AbovePointIndices)
        .Select(i => extraction.AbovePoints[i].SourcePointIndex).Distinct().ToArray();
    Require(rawIndices.Length > 1000 && rawIndices.All(i => scene.Labels[i] != Kind.Ground),
        "Raised top should seed the object while the real perimeter floor stays excluded.");
    checks.Add(new { name, groundP95ResidualMm = p95, retainedPoints = rawIndices.Length });
    Console.WriteLine($"PASS {name}: real floor p95 {p95:F3} mm.");
}

void CheckCancellation()
{
    const string name = "cancel-before-extraction";
    using var cancellation = new CancellationTokenSource();
    cancellation.Cancel();
    bool cancelled = false;
    try { AngleSteelExtractor.Process(Scenes.Ground().Cloud, cancellationToken: cancellation.Token); }
    catch (OperationCanceledException) { cancelled = true; }
    Require(cancelled, "Cancelled extraction must not complete as a valid scan.");
    checks.Add(new { name });
    Console.WriteLine($"PASS {name}.");
}

SegmentProcessingResult Process(Scene scene, string name, SegmentProcessingOptions? options = null)
{
    string dir = Path.Combine(work, name);
    Directory.CreateDirectory(dir);
    string input = Path.Combine(dir, "input.ply");
    ScanPly.Write(input, scene.Cloud.Header,
        scene.Cloud.Points.Select(p => (p.Position, p.ExtraColumns)), scene.Cloud.Points.Length,
        "Analytic synthetic fixture with independently labelled ground and roof samples", true);
    string before = Checksum(input);
    var result = new SegmentProcessor().ProcessSegment(input, Path.Combine(dir, "processed"), new Pose5(), processingOptions: options);
    Require(Checksum(input) == before, $"{name}: processing changed the source recording.");
    return result;
}

void Replay(string path, int number)
{
    string name = $"replay-{number:D2}-{Path.GetFileName(Path.GetDirectoryName(path))}";
    string before = Checksum(path);
    var result = new SegmentProcessor().ProcessSegment(path, Path.Combine(work, name), new Pose5());
    Require(Checksum(path) == before, "Real replay modified the original recording.");
    var source = ScanPly.Read(path);
    VerifyProvenance(source, result);
    var report = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(result.ProcessingReportPath));
    var calibration = report.GetProperty("extractionGroundCorrection");
    // ScanVector is a readonly struct: plain JSON deserialization would choose
    // its zero-valued default constructor, so read the reported basis explicitly.
    ScanVector Vector(string name)
    {
        var value = calibration.GetProperty(name);
        return new ScanVector(value.GetProperty("x").GetDouble(), value.GetProperty("y").GetDouble(), value.GetProperty("z").GetDouble());
    }
    var origin = Vector("origin");
    var xx = Vector("xAxis");
    var yy = Vector("yAxis");
    var zz = Vector("zAxis");
    double a = calibration.GetProperty("groundA").GetDouble(), b = calibration.GetProperty("groundB").GetDouble();
    double c = calibration.GetProperty("groundC").GetDouble(), zero = calibration.GetProperty("zZeroMm").GetDouble();
    var heights = result.SourcePointIndices.Select(i =>
    {
        var relative = source.Points[i].Position - origin;
        return relative.Dot(zz) - a * relative.Dot(xx) - b * relative.Dot(yy) - c - zero;
    }).Order().ToArray();
    int measuredLowBand = heights.Count(h => h <= 12);
    checks.Add(new
    {
        name, source = path, sourceHash = before, inputPoints = source.Points.Length,
        outputPoints = result.PointCount, result.SelectedComponentRank,
        result.Candidates, geometryAvailable = result.Geometry is not null,
        angle = result.Geometry?.WingNormalAngleDegrees, result.GeometryError,
        measuredLowBandPointsAtOrBelow12mm = measuredLowBand,
        minimumHeightAboveEstimatedGroundMm = heights.Length == 0 ? (double?)null : heights[0],
        medianHeightAboveEstimatedGroundMm = heights.Length == 0 ? (double?)null : heights[heights.Length / 2],
        groundCorrection = calibration,
    });
    Console.WriteLine($"REPLAY {path}: {source.Points.Length:N0} -> {result.PointCount:N0}, measured low band={measuredLowBand:N0}, wings={result.Geometry is not null}.");
    GC.Collect();
    GC.WaitForPendingFinalizers();
}

void VerifyProvenance(ScanCloud source, SegmentProcessingResult result)
{
    Require(result.SourcePointIndices.Length == result.SourcePointIndices.Distinct().Count(),
        "The same source vertex was duplicated in recovered edges.");
    var output = ScanPly.Read(result.ScannerPlyPath);
    Require(output.Points.Length == result.SourcePointIndices.Length, "Export count and source index count differ.");
    for (int i = 0; i < result.SourcePointIndices.Length; i++)
    {
        int raw = result.SourcePointIndices[i];
        Require(raw >= 0 && raw < source.Points.Length, "Export contains an invented source index.");
        var expected = source.Points[raw];
        var actual = output.Points[i];
        Require(actual.Position.X == expected.Position.X && actual.Position.Y == expected.Position.Y
            && actual.Position.Z == expected.Position.Z, $"Export vertex {i} was moved or synthesized.");
        Require(actual.ExtraColumns == expected.ExtraColumns + " " + raw.ToString(CultureInfo.InvariantCulture),
            $"Export vertex {i} lost or changed its original measurement attributes.");
    }
}

static string Checksum(string path)
{
    using var stream = File.OpenRead(path);
    return Convert.ToHexStringLower(SHA256.HashData(stream));
}
static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

enum Kind { Ground, Wing, LowWing, Top, ThicknessSide, NonWingSupport, Platform }
sealed record Scene(ScanCloud Cloud, Kind[] Labels, ScanVector ExpectedLongAxis);

static class Scenes
{
    const double GroundA = .08, GroundB = -.05, GroundC = -27;
    static double Floor(double x, double y) => GroundA * x + GroundB * y + GroundC;
    static double Noise(double x, double y) => .018 * Math.Sin(x * .17 + y * .31);

    public static Scene Roof(double angleDegrees)
    {
        double a = angleDegrees * Math.PI / 180, c = Math.Cos(a), s = Math.Sin(a);
        return Grid((x, y) =>
        {
            double u = c * x + s * y, v = -s * x + c * y;
            if (Math.Abs(u) > 40 || Math.Abs(v) > 70) return (0, Kind.Ground);
            double height = 42 - Math.Abs(u);
            return (height, height >= 2.5 && height <= 10.5 ? Kind.LowWing : Kind.Wing);
        }, new ScanVector(-s, c, GroundA * -s + GroundB * c).Normalize());
    }

    public static Scene Ground() => Grid((_, _) => (0, Kind.Ground), new ScanVector(0, 1, 0));

    public static Scene NoisyGroundRoof()
    {
        var points = new List<SourceScanPoint>();
        var labels = new List<Kind>();
        for (int y = -140; y <= 140; y++)
            for (int x = -110; x <= 110; x++)
            {
                bool wing = Math.Abs(x) <= 40 && Math.Abs(y) <= 70;
                double height = wing ? 42 - Math.Abs(x) : 0;
                // A small positive table-noise seam lies on the extension of
                // each fitted wing. It must not become recovered steel.
                double noise = wing ? Noise(x, y) : Math.Abs(x) == 41 && Math.Abs(y) <= 70
                    ? 1.05 : .60 * Math.Sin(x * .17 + y * .31);
                Kind label = !wing ? Kind.Ground : height >= 2.5 && height <= 10.5 ? Kind.LowWing : Kind.Wing;
                Add(points, labels, x, y, Floor(x, y) + height + noise, label);
            }
        return Make(points, labels, new ScanVector(0, 1, GroundB).Normalize());
    }

    public static Scene RoofWithConnectedSupport()
    {
        var scene = Grid((x, y) =>
        {
            if (Math.Abs(x) <= 40 && Math.Abs(y) <= 70)
            {
                double height = 42 - Math.Abs(x);
                return (height, height >= 2.5 && height <= 10.5 ? Kind.LowWing : Kind.Wing);
            }
            if (Math.Abs(x) <= 75 && y >= 71 && y <= 90) return (25.0, Kind.NonWingSupport);
            return (0.0, Kind.Ground);
        }, new ScanVector(0, 1, 0));
        // Isolate end/corner contamination from the separate, legitimate
        // projection of a tilted physical end onto a ridge-normal station.
        var points = scene.Cloud.Points.Select(p => new SourceScanPoint(p.SourcePointIndex,
            ScanPly.Quantize(new ScanVector(p.Position.X, p.Position.Y,
                p.Position.Z - GroundA * p.Position.X - GroundB * p.Position.Y)), p.ExtraColumns)).ToArray();
        return scene with { Cloud = new ScanCloud(scene.Cloud.Header, points) };
    }

    public static Scene SingleWing() => Grid((x, y) => x >= -40 && x <= 0 && Math.Abs(y) <= 70
        ? (42.0 + x, Kind.Wing) : (0.0, Kind.Ground), new ScanVector(0, 1, 0));

    public static Scene DominantTop() => Grid((x, y) =>
        Math.Abs(x) <= 94 && Math.Abs(y) <= 124 ? (30.0, Kind.Top) : (0.0, Kind.Ground), new ScanVector(0, 1, 0));

    public static Scene Plate()
    {
        var initial = Grid((x, y) => Math.Abs(x) <= 50 && Math.Abs(y) <= 70
            ? (30.0, Kind.Top) : (0.0, Kind.Ground), new ScanVector(0, 1, 0));
        var points = initial.Cloud.Points.ToList();
        var labels = initial.Labels.ToList();
        for (int y = -70; y <= 70; y++)
            for (int side = 0; side < 2; side++)
                for (int level = 0; level < 6; level++)
                {
                    double x = side == 0 ? -50 : 50;
                    Add(points, labels, x, y, Floor(x, y) + 27 + level * .5 + Noise(x, y), Kind.ThicknessSide);
                }
        return Make(points, labels, initial.ExpectedLongAxis);
    }

    static Scene Grid(Func<double, double, (double Height, Kind Label)> height, ScanVector longAxis)
    {
        var points = new List<SourceScanPoint>();
        var labels = new List<Kind>();
        for (int y = -140; y <= 140; y++)
            for (int x = -110; x <= 110; x++)
            {
                var selected = height(x, y);
                Add(points, labels, x, y, Floor(x, y) + selected.Height + Noise(x, y), selected.Label);
            }
        return Make(points, labels, longAxis);
    }

    static void Add(List<SourceScanPoint> points, List<Kind> labels, double x, double y, double z, Kind label)
    {
        int i = points.Count;
        points.Add(new SourceScanPoint(i, ScanPly.Quantize(new ScanVector(x, y, z)), ((i * 17) % 251).ToString(CultureInfo.InvariantCulture)));
        labels.Add(label);
    }

    static Scene Make(List<SourceScanPoint> points, List<Kind> labels, ScanVector longAxis)
    {
        string[] header = ["ply", "format ascii 1.0", "comment pitch_mm 1 1 0.01", $"element vertex {points.Count}",
            "property float x", "property float y", "property float z", "property uchar intensity", "end_header"];
        return new Scene(new ScanCloud(header, points.ToArray()), labels.ToArray(), longAxis);
    }
}
