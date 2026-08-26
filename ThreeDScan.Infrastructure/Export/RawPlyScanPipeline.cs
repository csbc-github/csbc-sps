using System.IO.Compression;
using Microsoft.Extensions.Options;
using PlyProcessor.Core;
using ThreeDScan.Domain.Abstractions;
using ThreeDScan.Domain.Models;
using ThreeDScan.Infrastructure.Configuration;
using ThreeDScan.Infrastructure.Scanners;

namespace ThreeDScan.Infrastructure.Export;

/// <summary>
/// 對齊 Hank Web API：raw PLY → <see cref="Pipeline.Run"/> → ZIP／JSON／PLY／GLB。
/// </summary>
public sealed class RawPlyScanPipeline : ISurfaceScanPipeline
{
    private readonly PlyProcessorOptions _options;

    public RawPlyScanPipeline(IOptions<PlyProcessorOptions> options)
    {
        _options = options.Value;
    }

    /// <inheritdoc />
    public Task<SurfaceScanPipelineResult> ProcessCaptureAsync(
        ScannerCaptureResult capture,
        CancellationToken ct = default)
    {
        return Task.Run(() => ProcessCapture(capture), ct);
    }

    private SurfaceScanPipelineResult ProcessCapture(ScannerCaptureResult capture)
    {
        if (string.IsNullOrWhiteSpace(capture.HeightImagePath))
        {
            throw new InvalidOperationException("擷取結果沒有高度影像路徑。");
        }

        var captureDir = Path.GetDirectoryName(capture.HeightImagePath)
            ?? throw new InvalidOperationException("無法解析擷取目錄。");

        if (!ScanCaptureCache.TryGet(capture.CaptureId, out var heightImage, out _))
        {
            throw new InvalidOperationException("擷取高度圖快取已失效，請重新掃描。");
        }

        var rawPlyPath = Path.Combine(captureDir, "raw_scene.ply");
        HeightImagePlyExporter.SaveAsAsciiPly(heightImage, rawPlyPath);

        var opts = _options.ToPipelineOptions();
        using var rawStream = File.OpenRead(rawPlyPath);
        var pipeline = Pipeline.Run(rawStream, "angle_steel", opts);

        var zipPath = Path.Combine(captureDir, "angle_steel_export.zip");
        var jsonPath = Path.Combine(captureDir, "angle_steel_coords.json");
        var plyPath = Path.Combine(captureDir, "angle_steel.ply");
        var glbPath = Path.Combine(captureDir, "angle_steel.glb");
        File.WriteAllBytes(zipPath, pipeline.ZipBytes);
        File.WriteAllBytes(jsonPath, pipeline.CoordsJsonBytes);

        var plyBytes = TryExtractFirstByExtension(pipeline.ZipBytes, ".ply");
        if (plyBytes is null || plyBytes.Length == 0)
        {
            throw new InvalidOperationException("Pipeline 未產出點雲 PLY。");
        }

        File.WriteAllBytes(plyPath, plyBytes);

        string? writtenGlb = null;
        var glbBytes = TryExtractFirstByExtension(pipeline.ZipBytes, ".glb");
        if (glbBytes is { Length: > 0 })
        {
            File.WriteAllBytes(glbPath, glbBytes);
            writtenGlb = glbPath;
        }

        var processedPts = CollectProcessedPoints(pipeline);
        var angle = AngleSteelAngleEstimator.Estimate(
            processedPts, Math.Max(1.0f, opts.NoiseVoxel));

        return new SurfaceScanPipelineResult
        {
            PointCloudPath = plyPath,
            ZipPath = zipPath,
            JsonPath = jsonPath,
            GlbPath = writtenGlb,
            MeanZ = ComputeMeanZ(pipeline),
            PointCount = processedPts.Length,
            AngleDegrees = angle?.AngleDegrees,
            AngleDeviationFromSquareDegrees = angle?.DeviationFromSquareDegrees,
        };
    }

    private static PlyPoint[] CollectProcessedPoints(PipelineResult pipeline)
    {
        var count = 0;
        foreach (var obj in pipeline.Objects)
        {
            count += obj.Points.Length;
        }

        var points = new PlyPoint[count];
        var index = 0;
        foreach (var obj in pipeline.Objects)
        {
            Array.Copy(obj.Points, 0, points, index, obj.Points.Length);
            index += obj.Points.Length;
        }

        return points;
    }

    private static double ComputeMeanZ(PipelineResult pipeline)
    {
        double sum = 0;
        var count = 0;
        foreach (var obj in pipeline.Objects)
        {
            foreach (var point in obj.Points)
            {
                sum += point.Z;
                count++;
            }
        }

        if (count == 0)
        {
            return 0;
        }

        return sum / count;
    }

    private static byte[]? TryExtractFirstByExtension(byte[] zipBytes, string extension)
    {
        using var ms = new MemoryStream(zipBytes);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        var entry = zip.Entries.FirstOrDefault(e =>
            e.Name.EndsWith(extension, StringComparison.OrdinalIgnoreCase));
        if (entry is null)
        {
            return null;
        }

        using var stream = entry.Open();
        using var outMs = new MemoryStream();
        stream.CopyTo(outMs);
        return outMs.ToArray();
    }
}
