using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ThreeDScan.Infrastructure.Export;
using ThreeDScan.Infrastructure.Export.Models;
using ThreeDScan.Infrastructure.Scanners;

string? output = null, referenceDll = null;
for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--output" && i + 1 < args.Length) output = Path.GetFullPath(args[++i]);
    else if (args[i] == "--reference" && i + 1 < args.Length) referenceDll = Path.GetFullPath(args[++i]);
    else throw new ArgumentException("Usage: [--output NEW_DIRECTORY] [--reference Ljs8.OneClick.dll]");
}
output ??= Path.Combine(Path.GetTempPath(), "ThreeDScan.HoleCapture.Regression", Guid.NewGuid().ToString("N"));
if (Directory.Exists(output)) throw new IOException("Choose a new output directory; existing files are never overwritten.");
Directory.CreateDirectory(output);
var checks = new List<object>();
var failures = new List<Exception>();
var timer = System.Diagnostics.Stopwatch.StartNew();
Check("native-structure-sizes-and-offsets", () =>
{
    Require(Marshal.SizeOf<HoleNativeEthernetConfig>() == 8, "Ethernet configuration size");
    Require(Marshal.SizeOf<HoleNativeHeightRequest>() == 16, "Height request size");
    Require(Marshal.SizeOf<HoleNativeHeightResponse>() == 32, "Height response size");
    Require(Marshal.SizeOf<HoleNativeHeightInfo>() == 32, "Height geometry size");
    Require(Marshal.SizeOf<HoleNativeProfileHeader>() == 24, "Profile header size");
    Require(Marshal.OffsetOf<HoleNativeHeightRequest>(nameof(HoleNativeHeightRequest.ProfileCount)).ToInt32() == 12, "Request count offset");
    Require(Marshal.OffsetOf<HoleNativeHeightResponse>(nameof(HoleNativeHeightResponse.CurrentImageCommitted)).ToInt32() == 30, "Committed offset");
    Require(Marshal.OffsetOf<HoleNativeHeightInfo>(nameof(HoleNativeHeightInfo.PitchZ)).ToInt32() == 28, "Z pitch offset");
});
Check("fresh-image-retries-with-uncommitted-valid-data", () =>
{
    var sdk = new FakeSdk();
    var frame = Capture(sdk);
    Require(frame.ImageNo == 8 && frame.ProfileCount == 2 && sdk.Reads.Count == 4, "Must reject stale/unavailable image and accept fresh valid image even when committed=0");
    Require(sdk.Reads[0].ProfileCount == 1 && sdk.Reads.Skip(1).All(r => r.ProfileCount == 1000 && r.Erase == 0), "Do not clear memory or request more than 1000");
    Require(sdk.Calls.SequenceEqual(["initialize", "open", "trigger", "close"]), "Default capture must not switch program or finalize shared SDK");
});
Check("actual-444-profile-count-is-retained", () =>
{
    var sdk = new FakeSdk { TotalProfiles = 444 };
    var frame = Capture(sdk);
    Require(frame.ProfileCount == 444 && frame.Heights.Length == 888, "444 measured rows must not be resampled, repeated, or expanded to 3108");
});
Check("1000-profile-request-limit-is-retained", () =>
{
    var frame = Capture(new FakeSdk { TotalProfiles = 3108 });
    Require(frame.ProfileCount == 1000 && frame.Heights.Length == 2000, "OneClick caps the requested image at 1000 actual rows");
});
Check("split-transfers-use-same-image-and-contiguous-profiles", () =>
{
    var sdk = new FakeSdk { Chunk = 1 };
    var frame = Capture(sdk);
    Require(frame.ProfileCount == 2 && frame.Heights.SequenceEqual(new ushort[] { 32767, 0, 32768, 65535 }), "Split height samples must match original sensor bytes");
    var request = sdk.Reads.Last();
    Require(request.PositionMode == 2 && request.ImageNo == 8 && request.ProfileNo == 1 && request.ProfileCount == 1, "Remainder must be requested by original image and exact next row");
});
Check("split-image-geometry-and-sequence-mismatch-are-rejected", () =>
{
    foreach (string fault in new[] { "image", "profile", "pitch", "luminance" })
        Throws<InvalidDataException>(() => Capture(new FakeSdk { Chunk = 1, SplitFault = fault }));
});
Check("missing-start-profile-is-rejected", () =>
    Throws<InvalidDataException>(() => Capture(new FakeSdk { MissingStart = true })));
Check("fatal-trigger-and-read-errors-clean-up", () =>
{
    foreach (var sdk in new[] { new FakeSdk { TriggerCode = 0x8049 }, new FakeSdk { ReadCode = 0x8042 } })
    {
        Throws<IOException>(() => Capture(sdk));
        Require(sdk.Calls.Last() == "close", "Connected failures must close only their connection");
    }
    var failedOpen = new FakeSdk { OpenCode = 0x1000 };
    Throws<IOException>(() => Capture(failedOpen));
    Require(!failedOpen.Calls.Contains("close"), "Do not close an unowned failed connection");
});
Check("stale-image-times-out-and-cleans-up", () =>
{
    var sdk = new FakeSdk { AlwaysStale = true };
    Throws<TimeoutException>(() => new HoleCaptureAcquisition(sdk).Capture(new("127.0.0.1", 24691, TimeoutSeconds: .005, PollSeconds: 0)));
    Require(sdk.Calls.Last() == "close", "Timeout must close capture connection");
});
Check("cancellation-is-observed-before-and-during-polling", () =>
{
    using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
    var untouched = new FakeSdk();
    Throws<OperationCanceledException>(() => new HoleCaptureAcquisition(untouched).Capture(Options(), cancelled.Token));
    Require(untouched.Calls.Count == 0, "Pre-cancelled capture must not initialize/open/trigger");
    using var during = new CancellationTokenSource();
    var sdk = new FakeSdk { OnTrigger = during.Cancel };
    Throws<OperationCanceledException>(() => new HoleCaptureAcquisition(sdk).Capture(Options(), during.Token));
    Require(sdk.Calls.Last() == "close", "Polling cancellation must close connection");
});
Check("program-only-changes-when-explicitly-requested", () =>
{
    var same = new FakeSdk();
    new HoleCaptureAcquisition(same).Capture(Options() with { ProgramNo = 2 });
    Require(same.Calls.Count(c => c == "get-program") == 1 && !same.Calls.Contains("change-program"), "Already active requested program must remain untouched");
    var different = new FakeSdk();
    new HoleCaptureAcquisition(different).Capture(Options() with { ProgramNo = 3 });
    Require(different.Calls.Count(c => c == "get-program") == 2 && different.Calls.Count(c => c == "change-program") == 1, "Explicit program must be switched and verified");
});
Check("native-buffer-header-geometry-contract", () =>
{
    var request = new HoleNativeHeightRequest { ProfileCount = 2 };
    var response = new HoleNativeHeightResponse { ImageNo = 9, TopProfileNo = 0, ProfileCount = 2 };
    var info = new HoleNativeHeightInfo { XPointCount = 2, YLineCount = 2, LuminanceOutput = 1,
        XStart = -32000000, YStart = -31300000, PitchX = 20000, PitchY = 140000, PitchZ = 1020 };
    var headers = new[] { new HoleNativeProfileHeader { ImageNo = 9, ProfileNo = 0 }, new HoleNativeProfileHeader { ImageNo = 9, ProfileNo = 1 } };
    ushort[] heights = [32767, 0, 32768, 65535, 55, 66]; byte[] luminance = [128, 0, 255, 1, 99, 99];
    var frame = HoleCaptureFrameBuilder.Build(request, response, info, headers, heights, luminance);
    Require(frame.Heights.SequenceEqual(heights.Take(4)) && frame.Luminance!.SequenceEqual(luminance.Take(4)), "Copy only actual packed sensor values");
    var badHeaders = headers.ToArray(); badHeaders[1].ImageNo++;
    Throws<InvalidDataException>(() => HoleCaptureFrameBuilder.Build(request, response, info, badHeaders, heights, luminance));
    badHeaders = headers.ToArray(); badHeaders[1].ProfileNo++;
    Throws<InvalidDataException>(() => HoleCaptureFrameBuilder.Build(request, response, info, badHeaders, heights, luminance));
    var excessive = info; excessive.XPointCount = 3201;
    Throws<InvalidDataException>(() => HoleCaptureFrameBuilder.Build(request, response, excessive, headers, heights, luminance));
    var oversized = response; oversized.ProfileCount = 3;
    Throws<InvalidDataException>(() => HoleCaptureFrameBuilder.Build(request, oversized, info, headers, heights, luminance));
    Throws<InvalidDataException>(() => HoleCaptureFrameBuilder.Build(request, response, info, headers, [1], luminance));
});
Check("oneclick-local-grid-export-byte-golden", () =>
{
    var frame = Capture(new FakeSdk());
    var heights = frame.Heights.ToArray();
    var oldCulture = CultureInfo.CurrentCulture;
    try
    {
        CultureInfo.CurrentCulture = new CultureInfo("de-DE");
        string path = Path.Combine(output, "local-grid.ply");
        HeightFramePlyExporter.SavePly(frame, path, PointCloudOrigin.LocalGrid);
        string expected = "0.000000 0.000000 -0.010200 128\n0.000000 1.400000 0.000000 255\n0.200000 1.400000 334.223400 1\n";
        Require(VertexBytes(path).SequenceEqual(Encoding.UTF8.GetBytes(expected)), "Local XY starts at zero, unsigned heights/luminance and F6 bytes must match supplied OneClick");
        Require(frame.Heights.SequenceEqual(heights), "Export must not modify sensor values");
        Throws<IOException>(() => HeightFramePlyExporter.SavePly(frame, path, PointCloudOrigin.LocalGrid));
    }
    finally { CultureInfo.CurrentCulture = oldCulture; }
});
Check("sdk-origin-export-remains-available-for-surface", () =>
{
    var path = Path.Combine(output, "sdk-origin.ply");
    HeightFramePlyExporter.SavePly(Capture(new FakeSdk()), path);
    Require(Encoding.UTF8.GetString(VertexBytes(path)).StartsWith("-320.000000 -313.000000 -0.010200 128\n", StringComparison.Ordinal), "Surface default export must retain SDK origin");
});
if (referenceDll is not null) Check("supplied-reference-dll-identical-measurement-bytes", () =>
{
    var reference = Assembly.LoadFile(referenceDll);
    var infoType = reference.GetType("Ljs8.OneClick.HeightInfo", true)!;
    object info = Activator.CreateInstance(infoType)!;
    var frame = Capture(new FakeSdk());
    foreach (var (name, value) in new Dictionary<string, object>
    {
        ["XPointCount"] = frame.Info.XPointCount, ["YLineCount"] = frame.Info.YLineCount,
        ["LuminanceOutput"] = frame.Info.LuminanceOutput, ["XStart"] = frame.Info.XStart,
        ["YStart"] = frame.Info.YStart, ["PitchX"] = frame.Info.PitchX,
        ["PitchY"] = frame.Info.PitchY, ["PitchZ"] = frame.Info.PitchZ,
    }) infoType.GetField(name)!.SetValue(info, value);
    object referenceFrame = Activator.CreateInstance(reference.GetType("Ljs8.OneClick.HeightFrame", true)!,
        info, frame.ImageNo, frame.FirstProfileNo, frame.ProfileCount, frame.Heights, frame.Luminance)!;
    string referencePath = Path.Combine(output, "supplied-reference.ply");
    reference.GetType("Ljs8.OneClick.PointCloudExporter", true)!.GetMethod("SavePly")!.Invoke(null, [referenceFrame, referencePath]);
    Require(VertexBytes(referencePath).SequenceEqual(VertexBytes(Path.Combine(output, "local-grid.ply"))), "Measurement bytes differ from the supplied OneClick exporter");
});

File.WriteAllText(Path.Combine(output, "summary.json"), JsonSerializer.Serialize(new
{
    success = failures.Count == 0, elapsedSeconds = timer.Elapsed.TotalSeconds, checks,
    referenceDll, referenceSha256 = referenceDll is null ? null : Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(referenceDll))),
}, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"{(failures.Count == 0 ? "PASS" : "FAIL")}: {checks.Count - failures.Count}/{checks.Count} checks; {output}");
return failures.Count == 0 ? 0 : 1;

void Check(string name, Action run)
{
    try { run(); checks.Add(new { name, passed = true }); Console.WriteLine("PASS " + name); }
    catch (Exception ex) { failures.Add(ex); checks.Add(new { name, passed = false, error = ex.ToString() }); Console.Error.WriteLine("FAIL " + name + ": " + ex.Message); }
}
static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
static void Throws<T>(Action run) where T : Exception
{
    try { run(); } catch (T) { return; }
    throw new InvalidOperationException("Expected " + typeof(T).Name);
}
static byte[] VertexBytes(string path)
{
    var bytes = File.ReadAllBytes(path); var marker = Encoding.ASCII.GetBytes("end_header\n");
    int index = bytes.AsSpan().IndexOf(marker);
    if (index < 0) throw new InvalidDataException("Missing PLY header terminator");
    return bytes[(index + marker.Length)..];
}
static HoleCaptureOptions Options() => new("127.0.0.1", 24691, PollSeconds: 0);
static HeightFrame Capture(FakeSdk sdk) => new HoleCaptureAcquisition(sdk).Capture(Options());

internal sealed class FakeSdk : IHoleCaptureSdk
{
    internal List<string> Calls { get; } = [];
    internal List<HoleNativeHeightRequest> Reads { get; } = [];
    internal int TotalProfiles = 2, Chunk, TriggerCode, OpenCode, ReadCode;
    internal bool AlwaysStale, MissingStart;
    internal string? SplitFault;
    internal Action? OnTrigger;
    private byte active = 2;
    public int Initialize() { Calls.Add("initialize"); return 0; }
    public int Open(ref HoleNativeEthernetConfig config) { Calls.Add("open"); return OpenCode; }
    public int GetActiveProgram(ref byte programNo) { Calls.Add("get-program"); programNo = active; return 0; }
    public int ChangeActiveProgram(byte programNo) { Calls.Add("change-program"); active = programNo; return 0; }
    public int Trigger() { Calls.Add("trigger"); OnTrigger?.Invoke(); return TriggerCode; }
    public int Close() { Calls.Add("close"); return 0; }
    public HoleCaptureReadResult Read(HoleNativeHeightRequest request)
    {
        Reads.Add(request);
        if (Reads.Count == 1) return new(0, new() { CurrentImageNo = 7 }, null);
        if (ReadCode != 0) return new(ReadCode, default, null);
        if (Reads.Count == 2) return new(0x80A0, default, null);
        uint image = Reads.Count == 3 || AlwaysStale ? 7u : 8u;
        int start = (int)request.ProfileNo;
        if (MissingStart) start++;
        int count = Math.Min(request.ProfileCount, TotalProfiles - start);
        if (Chunk > 0) count = Math.Min(count, Chunk);
        uint pitchZ = 1020;
        if (request.PositionMode == 2)
        {
            if (SplitFault == "image") image++;
            if (SplitFault == "profile") start++;
            if (SplitFault == "pitch") pitchZ++;
        }
        var info = new HeightInfo { XPointCount = 2, YLineCount = (ushort)TotalProfiles,
            LuminanceOutput = SplitFault == "luminance" && request.PositionMode == 2 ? (byte)0 : (byte)1,
            XStart = -32000000, YStart = -31300000, PitchX = 20000, PitchY = 140000, PitchZ = pitchZ };
        ushort[] source = [32767, 0, 32768, 65535]; byte[] brightness = [128, 0, 255, 1];
        var heights = Enumerable.Range(start * 2, count * 2).Select(i => source[i % 4]).ToArray();
        var luminance = Enumerable.Range(start * 2, count * 2).Select(i => brightness[i % 4]).ToArray();
        return new(0, new() { CurrentImageNo = image, ImageNo = image, ImageProfileCount = (uint)TotalProfiles,
            TopProfileNo = (uint)start, ProfileCount = (ushort)count, CurrentImageCommitted = 0 },
            new HeightFrame { Info = info, ImageNo = image, FirstProfileNo = (uint)start, ProfileCount = count,
                Heights = heights, Luminance = info.LuminanceOutput == 1 ? luminance : null });
    }
}
