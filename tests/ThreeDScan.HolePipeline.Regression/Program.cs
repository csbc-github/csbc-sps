using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using ThreeDScan.Application.Common;
using ThreeDScan.Application.Common.Models;
using ThreeDScan.Application.Scan;
using ThreeDScan.Domain.Abstractions;
using ThreeDScan.Domain.Models;
using ThreeDScan.Domain.Repositories;
using ThreeDScan.Infrastructure.Configuration;
using ThreeDScan.Infrastructure.Export;
using ThreeDScan.Infrastructure.Scan;

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
var output = Path.GetFullPath(args.Length == 2 && args[0] == "--output" ? args[1]
    : Path.Combine(Path.GetTempPath(), "ThreeDScan.HolePipeline.Regression", Guid.NewGuid().ToString("N")));
if (Directory.Exists(output)) throw new IOException("Choose a new output directory.");
Directory.CreateDirectory(output);
var source = Path.Combine(output, "input.ply");
var measurements = new List<(double X, double Y, double Z)>();
for (int row = 0; row <= 200; row++)
for (int col = 0; col <= 200; col++)
{
    double x = col * .5, y = row * .5;
    if ((x - 30) * (x - 30) + (y - 30) * (y - 30) < 64
        || (x - 72) * (x - 72) + (y - 65) * (y - 65) < 36) continue;
    measurements.Add((x, y, 20));
}
// Independently labelled visible ground and an upright wing establish the
// angle-steel workpiece before a request ROI narrows the hole analysis.
for (int row = -60; row <= 260; row++)
for (int col = -60; col <= 260; col++)
{
    double x = col * .5, y = row * .5;
    bool underWing = x is >= 0 and <= 100 && y is >= 0 and <= 100;
    bool throughHole = (x - 30) * (x - 30) + (y - 30) * (y - 30) < 64
        || (x - 72) * (x - 72) + (y - 65) * (y - 65) < 36;
    if (!underWing || throughHole) measurements.Add((x, y, 0));
}
for (int row = 0; row <= 200; row++)
for (int level = 1; level <= 90; level++) measurements.Add((0, row * .5, 20 + level * .5));
using (var writer = new StreamWriter(source))
{
    writer.WriteLine($"ply\nformat ascii 1.0\nelement vertex {measurements.Count}\nproperty float x\nproperty float y\nproperty float z\nend_header");
    foreach (var p in measurements) writer.WriteLine($"{p.X:R} {p.Y:R} {p.Z:R}");
}
var checks = new List<object>();
foreach (var scenario in new (string Id, int? Navigator, ScannerRoi? Roi)[]
{
    ("HOLE-ACTIVE", null, null),
    ("HOLE-EXPLICIT", 4, null),
    ("HOLE-SCANNER-ROI", null, new ScannerRoi
    {
        MinX = -2, MinY = 44, MinZ = 10, MaxX = 43, MaxY = 89, MaxZ = 30,
    }),
})
{
    var id = scenario.Id;
    var navigator = scenario.Navigator;
    int expectedHoles = scenario.Roi is null ? 2 : 1;
    var taskDir = Path.Combine(output, id);
    var storage = Proxy<IStorageSettings>((m, _) => m.Name switch
    {
        "get_ContentsRoot" => Path.Combine(output, id + "-contents"),
        "get_ExportRoot" => output,
        _ => throw new InvalidOperationException(m.Name),
    });
    var pose = new Pose5 { X = 1000, Y = 2000, Z = 3000, C = 90 };
    var doc = new TaskDocument
    {
        TaskId = id, Kind = "Hole", ScannerId = "TEST", Phase = "HoleCapture", Status = "Processing",
        BaseUrl = "https://example.invalid", CoordinatesSnapshot = new() { IsCalibrated = true },
        WorkRequest = JsonSerializer.SerializeToElement(new { cameraPosition = pose, navigatorScanNo = navigator, roi = scenario.Roi }),
    };
    var store = Proxy<IScanTaskRepository>((m, a) => m.Name switch
    {
        "GetAsync" => Task.FromResult(doc),
        "TaskDirectory" => taskDir,
        "UpdateAsync" => Update((Action<TaskDocument>)a![1]!),
        _ => throw new InvalidOperationException("Unexpected repository call: " + m.Name),
    });
    Task<TaskDocument> Update(Action<TaskDocument> mutation) { mutation(doc); return Task.FromResult(doc); }
    int resolutions = 0;
    var registry = Proxy<IScannerRegistry>((m, a) =>
    {
        if (m.Name == "TryGetEndpoint") { a![1] = new ScannerEndpoint { ScannerId = "TEST", Ip = "127.0.0.1" }; return true; }
        if (m.Name == "TryGetHoleRoi") throw new InvalidOperationException("A legacy station ROI silently restricted reference-compatible hole analysis.");
        if (m.Name == "ResolveProgramNo") { resolutions++; return (byte)((int)a![1]! - 1); }
        throw new InvalidOperationException("Unexpected registry call: " + m.Name);
    });
    var capture = new RecordedCapture(source);
    var factory = Proxy<IScannerCaptureFactory>((_, _) => capture);
    var orchestrator = new ScanOrchestrator(store, registry, factory,
        Unused<ICompensationRepository>(), Unused<IJobThicknessRepository>(), Unused<IDeviceCoordinator>(),
        Unused<IScanWorkQueue>(), storage, new ConfigurationBuilder().Build(),
        Options.Create(new SurfaceScanOrchestratorOptions()), Options.Create(new CallbackSettings()),
        Unused<ISystemRuntimeConfig>());
    await orchestrator.ExecuteHoleAsync(id, CancellationToken.None);
    Require(doc.Status == "Completed" && doc.Phase == "Completed", "The hole pipeline did not finish.");
    Require(capture.Calls == 1 && capture.Program == (navigator.HasValue ? (byte?)3 : null), "Hole capture selected an unexpected program.");
    Require(resolutions == (navigator.HasValue ? 1 : 0), "An omitted Navigator number must preserve the active program.");
    Require(doc.IsReplay && doc.Result!.Value.GetProperty("isReplay").GetBoolean(), "Recorded input lost its replay flag.");
    var result = doc.Result!.Value;
    Require(result.GetProperty("closedHoleCount").GetInt32() == expectedHoles, "The scanner ROI selected the wrong independently cut circles.");
    Require(result.GetProperty("features").GetArrayLength() == expectedHoles, "The API lost a separate hole.");
    foreach (var feature in result.GetProperty("features").EnumerateArray())
    {
        var native = feature.GetProperty("scannerBoundary").EnumerateArray().ToArray();
        var absolute = feature.GetProperty("boundary").EnumerateArray().ToArray();
        var indices = feature.GetProperty("boundarySourcePointIndices").EnumerateArray().ToArray();
        Require(native.Length > 0 && native.Length == absolute.Length && native.Length == indices.Length, "Boundary provenance arrays disagree.");
        for (int i = 0; i < native.Length; i++)
        {
            var p = native[i]; var q = absolute[i];
            double x = p.GetProperty("x").GetDouble(), y = p.GetProperty("y").GetDouble(), z = p.GetProperty("z").GetDouble();
            var original = measurements[indices[i].GetInt32()];
            Require(x == original.X && y == original.Y && z == original.Z, "A boundary was not an original measurement.");
            Require(Math.Abs(q.GetProperty("x").GetDouble() - (1000 - (y + 34))) < 1e-8
                && Math.Abs(q.GetProperty("y").GetDouble() - (2000 + x - 12)) < 1e-8
                && Math.Abs(q.GetProperty("z").GetDouble() - (3000 + z)) < 1e-8,
                "Local-grid SDK origin must be applied before the scanner pose rotation.");
        }
    }
    foreach (string artifact in new[] { "raw", "hole-result", "hole-centers", "hole-edges", "hole-boundaries" })
        Require(doc.Artifacts.TryGetValue(artifact, out var file) && File.Exists(file.Path), "A required result artifact is missing: " + artifact);
    foreach (string artifact in new[] { "hole-centers", "hole-edges", "hole-boundaries" })
    {
        using var json = JsonDocument.Parse(File.ReadAllBytes(doc.Artifacts[artifact].Path));
        Require(json.RootElement.GetProperty("holes").GetArrayLength() == expectedHoles, "Reference export discarded a hole.");
        Require(json.RootElement.GetProperty("expectedHoleCount").ValueKind == JsonValueKind.Null, "The API must not assume that every workpiece has three holes.");
    }
    var snapshot = new HoleBoundaryReader().TryRead(storage.ContentsRoot);
    Require(snapshot is not null && snapshot.RawPlyPath == doc.Artifacts["raw"].Path && snapshot.Points.Count > 0,
        "The API did not publish a usable monitor snapshot.");
    Require(snapshot!.AngleSteelOnly && snapshot.WorkpieceIdentified, "The monitor lost the validated angle-only scope.");
    checks.Add(new { id, nativeCaptureCalls = capture.Calls, nativeProgram = capture.Program, closedHoleCount = expectedHoles,
        publicBoundaryTransform = "SDK-origin translation before C=90 rotation", replay = doc.IsReplay, artifacts = doc.Artifacts.Keys.ToArray() });
    Console.WriteLine("PASS " + id);
}
var metadata = new SegmentCaptureMetadata { CoordinateOrigin = "sdk-origin", ScannerXStartMm = -12, ScannerYStartMm = 34 };
var unchangedMatrix = HoleCaptureTransforms.FromPose(new Pose5 { X = 10 }, new(), metadata);
Require(RigidTransforms.Apply(unchangedMatrix, new Point3 { X = 1, Y = 2, Z = 3 }).X == 11,
    "An existing SDK-origin PLY must not receive the origin twice.");
File.WriteAllText(Path.Combine(output, "summary.json"), JsonSerializer.Serialize(new { success = true, checks, sdkOriginCompatibility = true }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine("PASS hole pipeline; no network, device, SQL or callback used. " + output);

static void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
static T Proxy<T>(Func<MethodInfo, object?[]?, object?> invoke) where T : class
{
    var value = DispatchProxy.Create<T, TestProxy>(); ((TestProxy)(object)value).InvokeMethod = invoke; return value;
}
static T Unused<T>() where T : class => Proxy<T>((method, _) => throw new InvalidOperationException("Unexpected external dependency: " + method.Name));

public class TestProxy : DispatchProxy
{
    public Func<MethodInfo, object?[]?, object?> InvokeMethod { get; set; } = null!;
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => InvokeMethod(targetMethod!, args);
}
sealed class RecordedCapture(string source) : ISurfaceScannerCapture
{
    public string ScannerId => "TEST";
    public int Calls { get; private set; }
    public byte? Program { get; private set; }
    public Task<HoleRawCaptureResult> CaptureHoleAsync(byte? programNo = null, CancellationToken ct = default)
    {
        Calls++; Program = programNo;
        return Task.FromResult(new HoleRawCaptureResult
        {
            CaptureId = "recorded", RawPlyPath = source, IsReplay = true,
            Metadata = new() { Width = 201, ProfileCount = 201, PitchXmm = .5, PitchYmm = .5, PitchZmm = .01,
                CoordinateOrigin = "local-grid", ScannerXStartMm = -12, ScannerYStartMm = 34 },
        });
    }
    public Task<ScannerCaptureResult> CaptureAsync(byte programNo, CancellationToken ct = default) => throw new InvalidOperationException("The hole pipeline called the whole-image surface capture.");
    public Task<ScannerCaptureResult> CaptureWithAngleSteelAsync(byte programNo, CancellationToken ct = default) => throw new InvalidOperationException("The hole pipeline ran the angle-steel capture.");
}
