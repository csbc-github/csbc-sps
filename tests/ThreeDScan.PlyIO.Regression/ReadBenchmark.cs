using System.Diagnostics;
using System.Text.Json;
using ThreeDScan.Infrastructure.Export;
using ThreeDScan.Infrastructure.Export.Models;

internal static class ReadBenchmark
{
    internal static int Run(string sourcePath, string outputPath)
    {
        string source = Path.GetFullPath(sourcePath), output = Path.GetFullPath(outputPath);
        if (Directory.Exists(output)) throw new IOException("Use a new output directory.");
        Directory.CreateDirectory(output);
        var baseline = Read(() => BaselineScanPly.Read(source));
        var optimized = Read(() => ScanPly.Read(source));
        bool identical = baseline.Cloud.SourceSha256 == optimized.Cloud.SourceSha256
            && baseline.Cloud.Header.SequenceEqual(optimized.Cloud.Header)
            && baseline.Cloud.Points.Length == optimized.Cloud.Points.Length;
        if (!identical) throw new Exception("Read metadata differs.");
        for (int i = 0; i < baseline.Cloud.Points.Length; i++)
        {
            var a = baseline.Cloud.Points[i]; var b = optimized.Cloud.Points[i];
            if (a.SourcePointIndex != b.SourcePointIndex || a.ExtraColumns != b.ExtraColumns
                || BitConverter.DoubleToInt64Bits(a.Position.X) != BitConverter.DoubleToInt64Bits(b.Position.X)
                || BitConverter.DoubleToInt64Bits(a.Position.Y) != BitConverter.DoubleToInt64Bits(b.Position.Y)
                || BitConverter.DoubleToInt64Bits(a.Position.Z) != BitConverter.DoubleToInt64Bits(b.Position.Z))
                throw new Exception("Read point differs at " + i);
        }
        var report = new
        {
            source, sourceSha256 = baseline.Cloud.SourceSha256, points = baseline.Cloud.Points.Length,
            everyCoordinateBitAttributeAndIndexIdentical = true,
            baseline = new { baseline.Seconds, baseline.AllocatedBytes },
            optimized = new { optimized.Seconds, optimized.AllocatedBytes },
            speedup = baseline.Seconds / optimized.Seconds,
            allocationReductionFraction = 1 - optimized.AllocatedBytes / (double)baseline.AllocatedBytes,
            note = "One sequential pair in the same Release process; source is read-only. Independent full-pipeline benchmarking is authoritative for application latency.",
        };
        string json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(output, "summary.json"), json);
        Console.WriteLine(json);
        return 0;
    }

    private static (ScanCloud Cloud, double Seconds, long AllocatedBytes) Read(Func<ScanCloud> read)
    {
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        long before = GC.GetTotalAllocatedBytes(true);
        var time = Stopwatch.StartNew();
        var cloud = read();
        time.Stop();
        return (cloud, time.Elapsed.TotalSeconds, GC.GetTotalAllocatedBytes(true) - before);
    }
}
