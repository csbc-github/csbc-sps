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

    /// <summary>
    /// 由設定與範圍工廠建立管線。補償歷程透過 <paramref name="scopeFactory"/> 解析
    /// <see cref="ICompensationRepository"/>。
    /// </summary>
    /// <param name="options">PlyProcessor 設定（濾點、MeshThickness 等）。</param>
    /// <param name="scopeFactory">用來開短命 scope 讀最新補償。</param>
    public RawPlyScanPipeline(
        IOptions<PlyProcessorOptions> options,
        IServiceScopeFactory scopeFactory)
    {
        _options = options.Value;
        _scopeFactory = scopeFactory;
    }

    /// <inheritdoc />
    /// <remarks>
    /// 先讀該掃描器最新補償，再於執行緒池跑 CPU 密集的 PLY／網格匯出。
    /// <paramref name="thickness"/> 有值時量測兩翼；<paramref name="storedThickness"/> 有值時用存檔厚度組網格。
    /// </remarks>
    public async Task<SurfaceScanPipelineResult> ProcessCaptureAsync(
        ScannerCaptureResult capture,
        CancellationToken ct = default,
        ThicknessMeasureRequest? thickness = null,
        JobThicknessRecord? storedThickness = null)
    {
        var offset = await TryGetLatestOffsetAsync(capture.ScannerId, ct).ConfigureAwait(false);
        return await Task.Run(() => ProcessCapture(capture, offset, thickness, storedThickness), ct)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// 同步核心：高度圖 → raw PLY → 濾點 →（可選）補償平移 → 匯出 JSON／ZIP／PLY／GLB。
    /// </summary>
    /// <param name="capture">擷取結果，高度圖須仍在 <see cref="ScanCaptureCache"/>。</param>
    /// <param name="offset">該掃描器最新補償；null 表示公開檔與未補償座標相同。</param>
    /// <param name="thickness">夾具 A／B；null 表示不現場量測。</param>
    /// <param name="storedThickness">已存的兩翼厚度；null 表示不用存檔厚度。</param>
    /// <returns>檔案路徑與（若有量測）兩翼厚度。</returns>
    /// <exception cref="InvalidOperationException">缺高度圖、快取失效、或 ZIP 內沒有 PLY。</exception>
    private SurfaceScanPipelineResult ProcessCapture(
        ScannerCaptureResult capture,
        CompensationOffset? offset,
        ThicknessMeasureRequest? thickness,
        JobThicknessRecord? storedThickness)
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

        var baseOpts = _options.ToPipelineOptions();
        var measureOpts = baseOpts;
        if (thickness is not null)
        {
            measureOpts = baseOpts with
            {
                MeasureFlangeThickness = true,
                ThicknessAx = (float)thickness.Ax,
                ThicknessAy = (float)thickness.Ay,
                ThicknessAz = (float)thickness.Az,
                ThicknessBx = (float)thickness.Bx,
                ThicknessBy = (float)thickness.By,
                ThicknessBz = (float)thickness.Bz,
            };
        }
        else if (storedThickness is not null)
        {
            measureOpts = baseOpts with
            {
                UseStoredFlangeThickness = true,
                StoredThicknessA = (float)storedThickness.ThicknessA,
                StoredThicknessB = (float)storedThickness.ThicknessB,
                StoredThicknessAOnLowFlange = storedThickness.ThicknessAOnLowFlange,
            };
        }

        PlyPoint[] rawPts;
        using (var rawStream = File.OpenRead(rawPlyPath))
        {
            rawPts = PlyReader.Parse(rawStream).Points;
        }

        var processedPts = ScanProcessor.Process(rawPts, baseOpts.ZThresh, baseOpts.MinNoiseSize, baseOpts.NoiseVoxel);
        var jsonOpts = offset is not null ? baseOpts : measureOpts;
        var uncompensated = Pipeline.Export(rawPts, processedPts, "angle_steel", jsonOpts);

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
            export = Pipeline.Export(rawComp, processedComp, "angle_steel", measureOpts);
            compensatedJsonPath = Path.Combine(captureDir, "angle_steel_coords_compensated.json");
            File.WriteAllBytes(compensatedJsonPath, export.CoordsJsonBytes);
        }

        File.WriteAllBytes(zipPath, export.ZipBytes);
        WriteExtractedPlyAndGlb(export.ZipBytes, plyPath, glbPath, out var writtenGlb);

        var angle = AngleSteelAngleEstimator.Estimate(
            processedPts, Math.Max(1.0f, baseOpts.NoiseVoxel));

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
            ThicknessA = export.ThicknessA,
            ThicknessB = export.ThicknessB,
            ThicknessAOnLowFlange = export.ThicknessAOnLowFlange,
        };
    }

    /// <summary>
    /// 讀該掃描器最新一筆儀器補償（offset = 實際 − 預期）。
    /// 無歷程或掃描器編號空白時回 null，後續匯出不平移。
    /// </summary>
    /// <param name="scannerId">掃描器編號。</param>
    /// <param name="ct">取消權杖。</param>
    /// <returns>最新補償；沒有則 null。</returns>
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

    /// <summary>
    /// 將點雲每個點加上補償 offset（mm），得到公開座標框的點。
    /// </summary>
    /// <param name="points">未補償點。</param>
    /// <param name="offset">儀器校正 offset。</param>
    /// <returns>新陣列，不修改原點。</returns>
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

    /// <summary>
    /// 從 Pipeline ZIP 抽出第一個 .ply 寫到 <paramref name="plyPath"/>；
    /// 若有 .glb 則一併寫到 <paramref name="glbPath"/>。
    /// </summary>
    /// <param name="zipBytes">Pipeline 產出的 ZIP。</param>
    /// <param name="plyPath">點雲輸出絕對路徑。</param>
    /// <param name="glbPath">GLB 輸出絕對路徑（網格失敗時可能不寫）。</param>
    /// <param name="writtenGlb">實際寫出的 GLB 路徑；沒有 GLB 時為 null。</param>
    /// <exception cref="InvalidOperationException">ZIP 內找不到 PLY。</exception>
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

    /// <summary>
    /// 統計 Pipeline 所有分群物件的點數加總，供任務紀錄。
    /// </summary>
    /// <param name="pipeline">本次匯出結果。</param>
    /// <returns>點數；無物件時為 0。</returns>
    private static int CountPoints(PipelineResult pipeline)
    {
        var count = 0;
        foreach (var obj in pipeline.Objects)
        {
            count += obj.Points.Length;
        }

        return count;
    }

    /// <summary>
    /// 計算處理後點雲 Z 平均值（mm），供分段 <c>segmentZDelta</c>。
    /// </summary>
    /// <param name="pipeline">本次匯出結果。</param>
    /// <returns>平均 Z；沒有點時為 0。</returns>
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

    /// <summary>
    /// 在 ZIP 中找第一個副檔名相符（不分大小寫）的項目並讀成 byte[]。
    /// </summary>
    /// <param name="zipBytes">ZIP 內容。</param>
    /// <param name="extension">副檔名，例如 <c>.ply</c>、<c>.glb</c>。</param>
    /// <returns>檔案位元組；沒有該副檔名時為 null。</returns>
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
