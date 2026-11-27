using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ThreeDScan.Infrastructure.Export;
using ThreeDScan.Infrastructure.Export.Models;

if (args is ["--benchmark-read", var sourceFile, "--output", var benchmarkOutput])
{
    Environment.ExitCode = ReadBenchmark.Run(sourceFile, benchmarkOutput);
    return;
}

string output = args.Length == 2 && args[0] == "--output" ? Path.GetFullPath(args[1])
    : Path.Combine(Path.GetTempPath(), "ThreeDScan.PlyIO.Regression", Guid.NewGuid().ToString("N"));
if (Directory.Exists(output)) throw new IOException("Use a new output directory.");
Directory.CreateDirectory(output);
var results = new List<object>();
int failures = 0;
string[] xyz = ["ply", "format ascii 1.0", "element vertex 1", "property float x", "property float y", "property float z", "end_header"];
string[] attributes = [..xyz[..^1], "property uchar intensity", "property int raw_point_index", "end_header"];
Check("mixed numeric syntax and attribute whitespace", () =>
{
    string path = Path.Combine(output, "mixed.ply");
    var header = attributes.Select(s => s == "element vertex 1" ? "element vertex 5" : s).ToArray();
    File.WriteAllText(path, string.Join('\n', header) + "\n  +1.25e+2\t-0.0  2.2250738585072014E-308  001\t 12   opaque\twords  \n\n"
        + "0\u20031.2\u2007-2.3\u00a0003\u202f9\n"
        + " 1,234.5 -9.99e-6 +0.0000005 255 5\n"
        + "1 2 3 999 0 extra\n"
        + "0 0 -0 256 -1\n", new UTF8Encoding(true));
    SameClouds(path);
});
Check("three columns and undeclared extra tokens", () =>
{
    string path = WriteInput("three", xyz, "1 2 3   optional\tundeclared\n");
    SameClouds(path);
    if (ScanPly.Read(path).Points[0].ExtraColumns != "optional undeclared") throw new Exception("Extra token normalization changed.");
});
Check("byte intensity cache preserves exact token spellings", () =>
{
    string[] tokens = ["0", "1", "99", "255", "256", "999", "001", "01", "00", "+1", "-0", "1.0", "１２３"];
    var header = xyz[..^1].Select(s => s == "element vertex 1" ? $"element vertex {tokens.Length}" : s).Append("property uchar intensity").Append("end_header");
    string path = WriteInput("tokens", header, string.Join('\n', tokens.Select(s => "1 2 3 " + s)) + "\n");
    SameClouds(path);
    if (!ScanPly.Read(path).Points.Select(p => p.ExtraColumns).SequenceEqual(tokens)) throw new Exception("Attribute text changed.");
});
Check("hash includes BOM, mixed line endings and ignored trailing bytes", () =>
{
    foreach (bool bom in new[] { false, true })
    foreach (string ending in new[] { "\n", "\r\n", "\r" })
    {
        string path = Path.Combine(output, $"tail-{bom}-{ending.Length}-{(int)ending[0]}.ply");
        string tail = new string('x', 1_200_003) + "\nignored trailing content\u2003";
        File.WriteAllText(path, string.Join(ending, xyz) + ending + "1 2 3" + ending + tail, new UTF8Encoding(bom));
        SameClouds(path);
        if (ScanPly.Read(path).SourceSha256 != Hash(path)) throw new Exception("SHA omitted original bytes.");
    }
});
Check("UTF16 BOM and trailing elements remain compatible", () =>
{
    string path = Path.Combine(output, "utf16.ply");
    File.WriteAllText(path, string.Join('\n', xyz) + "\n-0 2 3\nnot a vertex\n", Encoding.Unicode);
    SameClouds(path);
});
Check("zero vertices still hash all bytes", () =>
{
    string path = WriteInput("zero", xyz.Select(s => s == "element vertex 1" ? "element vertex 0" : s), "ignored\n");
    SameClouds(path);
});
Check("malformed inputs retain baseline exception type and message", () =>
{
    var fixtures = new (string Name, string[] Header, string Data)[]
    {
        ("missing-header", ["ply", "format ascii 1.0"], ""),
        ("bad-format", xyz.Select(s => s.Replace("ascii", "binary_little_endian")).ToArray(), "1 2 3\n"),
        ("bad-order", xyz.Select(s => s == "property float x" ? "property float y" : s).ToArray(), "1 2 3\n"),
        ("list-property", [..xyz[..^1], "property list uchar int data", "end_header"], "1 2 3 0\n"),
        ("short-row-before-numeric", attributes, "invalid 2 3\n"),
        ("short-file", xyz, "\n\t\n"),
        ("bad-number", xyz, "invalid 2 3\n"),
        ("nan", xyz, "NaN 2 3\n"),
        ("infinite", xyz, "1 2 Infinity\n"),
        ("overflow-finite-check", xyz, "1E999 2 3\n"),
    };
    foreach (var f in fixtures)
    {
        string path = WriteInput("bad-" + f.Name, f.Header, f.Data);
        var baseline = Failure(() => BaselineScanPly.Read(path));
        var optimized = Failure(() => ScanPly.Read(path));
        if (baseline != optimized) throw new Exception($"{f.Name}: {baseline} != {optimized}");
    }
});
Check("quantization retains exact formatting rounds and negative zero", () =>
{
    foreach (double value in Values())
        SameBits(BaselineScanPly.Quantize(value), ScanPly.Quantize(value));
});
Check("writer is byte-identical in both precision modes", () =>
{
    var values = Values().ToArray();
    var points = values.Select((v, i) => (new ScanVector(v, values[(i + 5) % values.Length], values[(i + 13) % values.Length]), $"{i % 256} {i}\topaque")).ToArray();
    foreach (bool preserve in new[] { false, true })
    {
        string oldPath = Path.Combine(output, $"old-{preserve}.ply"), newPath = Path.Combine(output, $"new-{preserve}.ply");
        BaselineScanPly.Write(oldPath, attributes, points, points.Length, "identical comment", preserve);
        ScanPly.Write(newPath, attributes, points, points.Length, "identical comment", preserve);
        if (Hash(oldPath) != Hash(newPath)) throw new Exception("Writer bytes changed for preserve=" + preserve);
    }
});
Check("invariant behavior under noninvariant process culture", () =>
{
    var oldCulture = CultureInfo.CurrentCulture;
    try
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
        string path = WriteInput("culture", xyz, "1.25 2.5 3.75\n");
        SameClouds(path);
        SameBits(BaselineScanPly.Quantize(-1.23456789), ScanPly.Quantize(-1.23456789));
    }
    finally { CultureInfo.CurrentCulture = oldCulture; }
});
Check("failed writes and collisions preserve existing files and clean temporary output", () =>
{
    var one = new[] { (new ScanVector(1, 2, 3), "") };
    string shortPath = Path.Combine(output, "wrong-count.ply");
    _ = Failure(() => { ScanPly.Write(shortPath, xyz, one, 2, "bad count"); return null; });
    if (File.Exists(shortPath) || Directory.GetFiles(output, "*.tmp").Length != 0) throw new Exception("Failed write leaked file.");
    string existing = Path.Combine(output, "existing.ply");
    File.WriteAllText(existing, "keep");
    _ = Failure(() => { ScanPly.Write(existing, xyz, one, 1, "collision"); return null; });
    if (File.ReadAllText(existing) != "keep" || Directory.GetFiles(output, "*.tmp").Length != 0) throw new Exception("Collision changed existing output.");
});
Check("small allocation benchmark", () =>
{
    const int count = 100_000;
    var source = new double[count];
    for (int i = 0; i < source.Length; i++) source[i] = i * .0135792468 - 500;
    double sum = 0;
    foreach (double value in source.Take(1000)) sum += ScanPly.Quantize(value) + BaselineScanPly.Quantize(value);
    var baseline = Measure(() => { foreach (double value in source) sum += BaselineScanPly.Quantize(value); });
    var optimized = Measure(() => { foreach (double value in source) sum += ScanPly.Quantize(value); });
    Console.WriteLine($"Quantize {count}: baseline {baseline.ElapsedMs:F1}ms/{baseline.Bytes}B, optimized {optimized.ElapsedMs:F1}ms/{optimized.Bytes}B; checksum {sum:R}");
    if (optimized.Bytes > baseline.Bytes / 5) throw new Exception("Quantize retained per-value string allocations.");
});

File.WriteAllText(Path.Combine(output, "summary.json"), JsonSerializer.Serialize(new { checks = results.Count, failures, results }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"{(failures == 0 ? "PASS" : "FAIL")}: {results.Count - failures}/{results.Count}; {output}");
Environment.ExitCode = failures == 0 ? 0 : 1;

string WriteInput(string name, IEnumerable<string> header, string body)
{
    string path = Path.Combine(output, name + ".ply");
    File.WriteAllText(path, string.Join('\n', header) + "\n" + body, new UTF8Encoding(false));
    return path;
}
void Check(string name, Action test)
{
    try { test(); results.Add(new { name, passed = true }); Console.WriteLine("PASS " + name); }
    catch (Exception ex) { failures++; results.Add(new { name, passed = false, error = ex.ToString() }); Console.WriteLine("FAIL " + name + ": " + ex.Message); }
}
void SameClouds(string path)
{
    var a = BaselineScanPly.Read(path); var b = ScanPly.Read(path);
    if (!a.Header.SequenceEqual(b.Header) || a.SourceSha256 != b.SourceSha256 || a.Points.Length != b.Points.Length) throw new Exception("Cloud metadata changed.");
    for (int i = 0; i < a.Points.Length; i++)
    {
        var x = a.Points[i]; var y = b.Points[i];
        SameBits(x.Position.X, y.Position.X); SameBits(x.Position.Y, y.Position.Y); SameBits(x.Position.Z, y.Position.Z);
        if (x.SourcePointIndex != y.SourcePointIndex || x.ExtraColumns != y.ExtraColumns) throw new Exception("Point provenance or attributes changed.");
    }
}
void SameBits(double expected, double actual)
{
    if (BitConverter.DoubleToInt64Bits(expected) != BitConverter.DoubleToInt64Bits(actual)) throw new Exception($"Double bits changed: {expected:R} vs {actual:R}");
}
string Failure(Func<object?> test)
{
    try { _ = test(); return "NO ERROR"; }
    catch (Exception ex) { return ex.GetType().FullName + ": " + ex.Message; }
}
string Hash(string path) { using var stream = File.OpenRead(path); return Convert.ToHexStringLower(SHA256.HashData(stream)); }
(double ElapsedMs, long Bytes) Measure(Action action)
{
    long before = GC.GetAllocatedBytesForCurrentThread(); var time = Stopwatch.StartNew(); action(); time.Stop();
    return (time.Elapsed.TotalMilliseconds, GC.GetAllocatedBytesForCurrentThread() - before);
}
IEnumerable<double> Values()
{
    double[] special = [0, -0d, BitConverter.Int64BitsToDouble(long.MinValue), double.Epsilon, -double.Epsilon, double.MaxValue, double.MinValue,
        double.PositiveInfinity, double.NegativeInfinity, double.NaN, 1.2345675, -1.2345675, .0000005, -.0000005,
        100000000000000.5, -100000000000000.5, 1.0000005000000001, 1.0000004999999998];
    foreach (double value in special) yield return value;
    var random = new Random(729031);
    for (int i = 0; i < 5000; i++)
    {
        long bits = random.NextInt64();
        double value = BitConverter.Int64BitsToDouble(bits);
        if (double.IsFinite(value)) { yield return value; yield return -value; }
        double tie = random.Next(-1000000, 1000000) / 1_000_000d + .0000005;
        yield return tie; yield return Math.BitIncrement(tie); yield return Math.BitDecrement(tie);
    }
}
