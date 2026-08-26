using System.IO.Compression;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PlyProcessor.Core;
using ThreeDScan.Domain.Abstractions;
using ThreeDScan.Domain.Models;
using ThreeDScan.Domain.Repositories;
using ThreeDScan.Infrastructure.Configuration;
using ThreeDScan.Infrastructure.Scanners;

namespace ThreeDScan.Infrastructure.Export;

/// <summary>
/// raw PLY → <see cref="Pipeline.Run"/>／<see cref="Pipeline.Export"/> → ZIP／PLY／JSON／GLB。
/// 有儀器校正時：原始 JSON 另存，PLY／ZIP／GLB／補償 JSON 為套 offset 後的結果。
/// </summary>
public sealed class RawPlyScanPipeline : ISurfaceScanPipeline
{
    private readonly PlyProcessorOptions _options;
    private readonly IServiceScopeFactory _scopeFactory;

    public RawPlyScanPipeline(
        IOptions<PlyProcessorOptions> options,
        IServiceScopeFactory scopeFactory)
    {
        _options = options.Value;
        _scopeFactory = scopeFactory;
    }

    /// <inheritdoc />
    public async Task<SurfaceScanPipelineResult> ProcessCaptureAsync(
        ScannerCaptureResult capture,
        CancellationToken ct = default)
    {
        var offset = await TryGetLatestOffsetAsync(capture.ScannerId, ct).ConfigureAwait(false);
        return await Task.Run(() => ProcessCapture(capture, offset), ct).ConfigureAwait(false);
    }

    private SurfaceScanPipelineResult ProcessCapture(
        ScannerCaptureResult capture,
        CompensationOffset? offset)
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
        PlyPoint[] rawPts;
        using (var rawStream = File.OpenRead(rawPlyPath))
        {
            rawPts = PlyReader.Parse(rawStream).Points;
        }

        var processedPts = ScanProcessor.Process(rawPts, opts.ZThresh, opts.MinNoiseSize, opts.NoiseVoxel);
        var uncompensated = Pipeline.Export(rawPts, processedPts, "angle_steel", opts);

        var zipPath = Path.Combine(captureDir, "angle_steel_export.zip");
        var jsonPath = Path.Combine(captureDir, "angle_steel_coords.json");
        var plyPath = Path.Combine(captureDir, "angle_steel.ply");
        var glbPath = Path.Combine(captureDir, "angle_steel.glb");
        File.WriteAllBytes(jsonPath, uncompensated.CoordsJsonBytes);

        var export = uncompensated;
        string? compensatedJsonPath = null;
        if (offset is not null)
        {
            var rawComp = Translate(rawPts, offset);
            var processedComp = Translate(processedPts, offset);
            export = Pipeline.Export(rawComp, processedComp, "angle_steel", opts);
            compensatedJsonPath = Path.Combine(captureDir, "angle_steel_coords_compensated.json");
            File.WriteAllBytes(compensatedJsonPath, export.CoordsJsonBytes);
        }

        File.WriteAllBytes(zipPath, export.ZipBytes);
        WriteExtractedPlyAndGlb(export.ZipBytes, plyPath, glbPath, out var writtenGlb);

        var angle = AngleSteelAngleEstimator.Estimate(
            processedPts, Math.Max(1.0f, opts.NoiseVoxel));

        return new SurfaceScanPipelineResult
        {
            PointCloudPath = plyPath,
            ZipPath = zipPath,
            JsonPath = jsonPath,
            CompensatedJsonPath = compensatedJsonPath,
            GlbPath = writtenGlb,
            MeanZ = MeanZ(export),
            PointCount = CountPoints(export),
            AngleDegrees = angle?.AngleDegrees,
            AngleDeviationFromSquareDegrees = angle?.DeviationFromSquareDegrees,
        };
    }

    private async Task<CompensationOffset?> TryGetLatestOffsetAsync(string scannerId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(scannerId))
        {
            return null;
        }

        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ICompensationRepository>();
        return await repo.GetLatestAsync(scannerId, ct).ConfigureAwait(false);
    }

    private static PlyPoint[] Translate(PlyPoint[] points, CompensationOffset offset)
    {
        var ox = (float)offset.OffsetX;
        var oy = (float)offset.OffsetY;
        var oz = (float)offset.OffsetZ;
        var copy = new PlyPoint[points.Length];
        for (var i = 0; i < points.Length; i++)
        {
            var p = points[i];
            copy[i] = new PlyPoint(p.X + ox, p.Y + oy, p.Z + oz, p.Intensity);
        }

        return copy;
    }

    private static void WriteExtractedPlyAndGlb(
        byte[] zipBytes,
        string plyPath,
        string glbPath,
        out string? writtenGlb)
    {
        var plyBytes = TryExtractFirstByExtension(zipBytes, ".ply");
        if (plyBytes is null || plyBytes.Length == 0)
        {
            throw new InvalidOperationException("Pipeline 未產出點雲 PLY。");
        }

        File.WriteAllBytes(plyPath, plyBytes);

        writtenGlb = null;
        var glbBytes = TryExtractFirstByExtension(zipBytes, ".glb");
        if (glbBytes is { Length: > 0 })
        {
            File.WriteAllBytes(glbPath, glbBytes);
            writtenGlb = glbPath;
        }
    }

    private static int CountPoints(PipelineResult pipeline)
    {
        var count = 0;
        foreach (var obj in pipeline.Objects)
        {
            count += obj.Points.Length;
        }

        return count;
    }

    private static double MeanZ(PipelineResult pipeline)
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
