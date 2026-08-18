using System.IO.Compression;
using Microsoft.Extensions.Options;
using PlyProcessor.Core;
using ThreeDScan.Infrastructure.Configuration;

namespace ThreeDScan.Infrastructure.Export;

/// <summary>
/// 對齊 Hank WinForms：Python 角鋼 PLY → 角度估算 → <see cref="Pipeline.RunProcessed"/> → ZIP/JSON/GLB。
/// </summary>
public sealed class AngleSteelPipelineExporter
{
    private readonly PlyProcessorOptions _options;

    public AngleSteelPipelineExporter(IOptions<PlyProcessorOptions> options)
    {
        _options = options.Value;
    }

    public AngleSteelExportResult Export(string processedPlyPath, string captureDir, string exportBaseName)
    {
        var opts = _options.ToPipelineOptions();
        using var pointStream = File.OpenRead(processedPlyPath);
        var points = PlyReader.Parse(pointStream).Points;
        var angle = AngleSteelAngleEstimator.Estimate(
            points, Math.Max(1.0f, opts.NoiseVoxel));

        using var pipelineStream = File.OpenRead(processedPlyPath);
        var pipeline = Pipeline.RunProcessed(pipelineStream, exportBaseName, opts);

        var zipPath = Path.Combine(captureDir, $"{exportBaseName}_export.zip");
        var jsonPath = Path.Combine(captureDir, $"{exportBaseName}_coords.json");
        var glbPath = Path.Combine(captureDir, $"{exportBaseName}.glb");
        File.WriteAllBytes(zipPath, pipeline.ZipBytes);
        File.WriteAllBytes(jsonPath, pipeline.CoordsJsonBytes);

        string? writtenGlb = null;
        var glbBytes = TryExtractFirstGlb(pipeline.ZipBytes);
        if (glbBytes is { Length: > 0 })
        {
            File.WriteAllBytes(glbPath, glbBytes);
            writtenGlb = glbPath;
        }

        return new AngleSteelExportResult
        {
            ZipPath = zipPath,
            JsonPath = jsonPath,
            GlbPath = writtenGlb,
            ObjectCount = pipeline.Objects.Count,
            AngleDegrees = angle?.AngleDegrees,
            DeviationFromSquareDegrees = angle?.DeviationFromSquareDegrees,
            FirstPlanePoints = angle?.FirstPlanePoints,
            SecondPlanePoints = angle?.SecondPlanePoints,
        };
    }

    static byte[]? TryExtractFirstGlb(byte[] zipBytes)
    {
        using var ms = new MemoryStream(zipBytes);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        var entry = zip.Entries.FirstOrDefault(e =>
            e.Name.EndsWith(".glb", StringComparison.OrdinalIgnoreCase));
        if (entry is null) return null;
        using var stream = entry.Open();
        using var outMs = new MemoryStream();
        stream.CopyTo(outMs);
        return outMs.ToArray();
    }
}

public sealed class AngleSteelExportResult
{
    public required string ZipPath { get; init; }
    public required string JsonPath { get; init; }
    public string? GlbPath { get; init; }
    public int ObjectCount { get; init; }
    public double? AngleDegrees { get; init; }
    public double? DeviationFromSquareDegrees { get; init; }
    public int? FirstPlanePoints { get; init; }
    public int? SecondPlanePoints { get; init; }
}
