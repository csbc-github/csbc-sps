using System.IO.Compression;
using System.Text.Json;
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
/// raw PLY → ScanProcessor.Process → <see cref="Pipeline.Export"/> → ZIP／PLY／JSON／GLB。
/// 公開點雲／ZIP／GLB 為相對座標加上 cameraPosition 與儀器補償；相對 JSON 另存。
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
        JobThicknessRecord? storedThickness = null,
        CameraPosition? cameraPosition = null)
    {
        var offset = await TryGetLatestOffsetAsync(capture.ScannerId, ct).ConfigureAwait(false);
        return await Task.Run(
            () => ProcessCapture(capture, offset, thickness, storedThickness, cameraPosition),
            ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<SurfaceScanPipelineResult> ProcessRawPlyAsync(
        string scannerId,
        string rawPlyPath,
        string outputDirectory,
        CancellationToken ct = default,
        ThicknessMeasureRequest? thickness = null,
        JobThicknessRecord? storedThickness = null,
        CameraPosition? cameraPosition = null)
    {
        var offset = await TryGetLatestOffsetAsync(scannerId, ct).ConfigureAwait(false);
        return await Task.Run(
            () => ProcessRawPly(
                rawPlyPath,
                outputDirectory,
                offset,
                thickness,
                storedThickness,
                cameraPosition),
            ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<SurfaceScanPipelineResult> ExportStitchedAsync(
        IReadOnlyList<string> segmentPlyPaths,
        string outputDirectory,
        CancellationToken ct = default,
        JobThicknessRecord? storedThickness = null)
    {
        return Task.Run(() => ExportStitched(segmentPlyPaths, outputDirectory, storedThickness), ct);
    }

    /// <summary>
    /// 讀各段 PLY、沿 X 拼接，再 <see cref="Pipeline.Export"/> 寫入 <paramref name="outputDirectory"/>。
    /// </summary>
    private SurfaceScanPipelineResult ExportStitched(
        IReadOnlyList<string> segmentPlyPaths,
        string outputDirectory,
        JobThicknessRecord? storedThickness)
    {
        if (segmentPlyPaths is null || segmentPlyPaths.Count == 0)
        {
            throw new InvalidOperationException("拼接失敗：沒有段點雲路徑。");
        }

        Directory.CreateDirectory(outputDirectory);

        var segments = new List<PlyPoint[]>();
        for (int i = 0; i < segmentPlyPaths.Count; i++)
        {
            var path = segmentPlyPaths[i];
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                throw new InvalidOperationException($"拼接失敗：找不到段點雲 {path}。");
            }

            using var stream = File.OpenRead(path);
            var pts = PlyReader.Parse(stream).Points;
            if (pts.Length > 0)
            {
                segments.Add(pts);
            }
        }

        var stitched = ScanStitcher.Stitch(segments, _options.ToScanStitchOptions());
        var baseOpts = _options.ToPipelineOptions();
        var exportOpts = baseOpts;
        if (storedThickness is not null)
        {
            exportOpts = baseOpts with
            {
                UseStoredFlangeThickness = true,
                StoredThicknessA = (float)storedThickness.ThicknessA,
                StoredThicknessB = (float)storedThickness.ThicknessB,
                StoredThicknessAOnLowFlange = storedThickness.ThicknessAOnLowFlange,
            };
        }

        var export = Pipeline.Export(stitched, stitched, "angle_steel", exportOpts);
        var zipPath = Path.Combine(outputDirectory, "angle_steel_export.zip");
        var jsonPath = Path.Combine(outputDirectory, "angle_steel_coords.json");
        var plyPath = Path.Combine(outputDirectory, "angle_steel.ply");
        var glbPath = Path.Combine(outputDirectory, "angle_steel.glb");
        File.WriteAllBytes(jsonPath, export.CoordsJsonBytes);
        File.WriteAllBytes(zipPath, export.ZipBytes);
        WriteExtractedPlyAndGlb(export.ZipBytes, plyPath, glbPath, out var writtenGlb);

        var head = BuildHeadFeatures(export, 0, 0, 0);
        WriteHeadFeaturesSidecar(
            outputDirectory,
            head,
            storedThickness?.ThicknessA,
            storedThickness?.ThicknessB);

        var angle = AngleSteelAngleEstimator.Estimate(
            stitched, Math.Max(1.0f, baseOpts.NoiseVoxel));

        return new SurfaceScanPipelineResult
        {
            PointCloudPath = plyPath,
            ZipPath = zipPath,
            JsonPath = jsonPath,
            CompensatedJsonPath = null,
            GlbPath = writtenGlb,
            MeanZ = MeanZ(export),
            HeadFeatures = head,
            PointCount = CountPoints(export),
            AngleDegrees = angle?.AngleDegrees,
            AngleDeviationFromSquareDegrees = angle?.DeviationFromSquareDegrees,
            ThicknessA = storedThickness?.ThicknessA,
            ThicknessB = storedThickness?.ThicknessB,
            ThicknessAOnLowFlange = storedThickness?.ThicknessAOnLowFlange,
            CompensationApplied = false,
        };
    }

    /// <summary>
    /// 同步核心：高度圖 → raw PLY → 濾點 →（可選）平移到公開絕對座標 → 匯出 JSON／ZIP／PLY／GLB。
    /// </summary>
    /// <param name="capture">擷取結果，高度圖須仍在 <see cref="ScanCaptureCache"/>。</param>
    /// <param name="offset">該掃描器最新補償；null 表示不加補償，仍可加 cameraPosition。</param>
    /// <param name="thickness">夾具 A／B；null 表示不現場量測。</param>
    /// <param name="storedThickness">已存的兩翼厚度；null 表示不用存檔厚度。</param>
    /// <param name="cameraPosition">DAS 期望掃描器到位座標；null 視為 (0,0,0)。</param>
    /// <returns>檔案路徑與（若有量測）兩翼厚度。</returns>
    /// <exception cref="InvalidOperationException">缺高度圖、快取失效、或 ZIP 內沒有 PLY。</exception>
    private SurfaceScanPipelineResult ProcessCapture(
        ScannerCaptureResult capture,
        CompensationOffset? offset,
        ThicknessMeasureRequest? thickness,
        JobThicknessRecord? storedThickness,
        CameraPosition? cameraPosition)
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
        return ProcessRawPly(rawPlyPath, captureDir, offset, thickness, storedThickness, cameraPosition);
    }

    /// <summary>
    /// 讀 raw PLY → 濾點 → 相對 JSON → 平移到公開絕對座標後量厚度並匯出 PLY／ZIP／GLB。
    /// 與現場掃描寫出 raw 之後的步驟相同。
    /// </summary>
    private SurfaceScanPipelineResult ProcessRawPly(
        string rawPlyPath,
        string outputDirectory,
        CompensationOffset? offset,
        ThicknessMeasureRequest? thickness,
        JobThicknessRecord? storedThickness,
        CameraPosition? cameraPosition)
    {
        var baseOpts = _options.ToPipelineOptions();
        var measureOpts = baseOpts;
        if (thickness is not null)
        {
            measureOpts = baseOpts with
            {
                MeasureFlangeThickness = true,
                HasInnerLeft = thickness.HasInnerLeft,
                InnerLeftX = (float)thickness.InnerLeftX,
                InnerLeftY = (float)thickness.InnerLeftY,
                InnerLeftZ = (float)thickness.InnerLeftZ,
                HasInnerRight = thickness.HasInnerRight,
                InnerRightX = (float)thickness.InnerRightX,
                InnerRightY = (float)thickness.InnerRightY,
                InnerRightZ = (float)thickness.InnerRightZ,
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

        Directory.CreateDirectory(outputDirectory);

        PlyPoint[] rawPts;
        using (var rawStream = File.OpenRead(rawPlyPath))
        {
            rawPts = PlyReader.Parse(rawStream).Points;
        }

        var processedPts = ScanProcessor.Process(rawPts, baseOpts.MinNoiseSize, baseOpts.NoiseVoxel);

        ResolveTranslation(cameraPosition, offset, out double dx, out double dy, out double dz);
        var jsonOpts = HasTranslation(dx, dy, dz) ? baseOpts : measureOpts;
        var relative = Pipeline.Export(rawPts, processedPts, "angle_steel", jsonOpts);

        var zipPath = Path.Combine(outputDirectory, "angle_steel_export.zip");
        var jsonPath = Path.Combine(outputDirectory, "angle_steel_coords.json");
        var plyPath = Path.Combine(outputDirectory, "angle_steel.ply");
        var glbPath = Path.Combine(outputDirectory, "angle_steel.glb");
        File.WriteAllBytes(jsonPath, relative.CoordsJsonBytes);

        var export = relative;
        string? compensatedJsonPath = null;
        if (HasTranslation(dx, dy, dz))
        {
            var rawAbs = Translate(rawPts, dx, dy, dz);
            var processedAbs = Translate(processedPts, dx, dy, dz);
            export = Pipeline.Export(rawAbs, processedAbs, "angle_steel", measureOpts);
            compensatedJsonPath = Path.Combine(outputDirectory, "angle_steel_coords_compensated.json");
            File.WriteAllBytes(compensatedJsonPath, export.CoordsJsonBytes);
        }

        File.WriteAllBytes(zipPath, export.ZipBytes);
        WriteExtractedPlyAndGlb(export.ZipBytes, plyPath, glbPath, out var writtenGlb);

        var head = BuildHeadFeatures(relative, dx, dy, dz);
        WriteHeadFeaturesSidecar(outputDirectory, head, export.ThicknessA, export.ThicknessB);

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
            HeadFeatures = head,
            PointCount = CountPoints(export),
            AngleDegrees = angle?.AngleDegrees,
            AngleDeviationFromSquareDegrees = angle?.DeviationFromSquareDegrees,
            ThicknessA = export.ThicknessA,
            ThicknessB = export.ThicknessB,
            ThicknessAOnLowFlange = export.ThicknessAOnLowFlange,
            CompensationApplied = offset is not null,
        };
    }

    /// <summary>
    /// 在掃描器相對座標上取前緣三點，再平移到公開絕對座標。
    /// </summary>
    /// <param name="export">相對座標的 Pipeline 匯出（分群後角鋼點）。</param>
    /// <param name="dx">公開框 X 平移 (mm)。</param>
    /// <param name="dy">公開框 Y 平移 (mm)。</param>
    /// <param name="dz">公開框 Z 平移 (mm)。</param>
    /// <returns>前緣三點與包角線 Z 差；點太少時為 null。</returns>
    static HeadFeaturesRecord? BuildHeadFeatures(PipelineResult export, double dx, double dy, double dz)
    {
        if (export.Objects.Count == 0 || export.Objects[0].Points.Length == 0)
        {
            return null;
        }

        var extracted = HeadFeatureExtractor.Extract(export.Objects[0].Points);
        if (extracted is null)
        {
            return null;
        }

        return new HeadFeaturesRecord
        {
            Kind = extracted.Kind,
            LeftX = extracted.LeftX + dx,
            LeftY = extracted.LeftY + dy,
            LeftZ = extracted.LeftZ + dz,
            ApexX = extracted.ApexX + dx,
            ApexY = extracted.ApexY + dy,
            ApexZ = extracted.ApexZ + dz,
            RightX = extracted.RightX + dx,
            RightY = extracted.RightY + dy,
            RightZ = extracted.RightZ + dz,
            RidgeZDelta = extracted.RidgeZDelta,
            Length = extracted.SpanMm,
        };
    }

    /// <summary>
    /// 與 GLB 同目錄寫前緣三點、厚度與包角線 Z 差，畫面載入網格時用來標註與顯示。
    /// </summary>
    static void WriteHeadFeaturesSidecar(
        string outputDirectory,
        HeadFeaturesRecord? head,
        double? thicknessLeft = null,
        double? thicknessRight = null)
    {
        var path = Path.Combine(outputDirectory, "angle_steel_head_features.json");
        if (head is null)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            return;
        }

        using var stream = File.Create(path);
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
        writer.WriteStartObject();
        writer.WriteString("kind", head.Kind);
        WriteXyz(writer, "leftCorner", head.LeftX, head.LeftY, head.LeftZ);
        WriteXyz(writer, "apex", head.ApexX, head.ApexY, head.ApexZ);
        WriteXyz(writer, "rightCorner", head.RightX, head.RightY, head.RightZ);
        writer.WriteNumber("ridgeZDelta", head.RidgeZDelta);
        writer.WriteNumber("length", head.Length);
        if (thicknessLeft.HasValue)
        {
            writer.WriteNumber("thicknessLeft", thicknessLeft.Value);
        }

        if (thicknessRight.HasValue)
        {
            writer.WriteNumber("thicknessRight", thicknessRight.Value);
        }

        writer.WriteEndObject();
        writer.Flush();
    }

    static void WriteXyz(Utf8JsonWriter writer, string name, double x, double y, double z)
    {
        writer.WritePropertyName(name);
        writer.WriteStartObject();
        writer.WriteNumber("x", x);
        writer.WriteNumber("y", y);
        writer.WriteNumber("z", z);
        writer.WriteEndObject();
    }

    /// <summary>
    /// 讀該掃描器最新一筆儀器補償（offset = 實際 − 預期）。
    /// 無歷程或掃描器編號空白時回 null；公開檔仍可只加 cameraPosition。
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
    /// 將點雲每個點加上平移 (mm)，得到公開座標框的點。
    /// </summary>
    /// <param name="points">相對座標點。</param>
    /// <param name="dx">X 平移 (mm)。</param>
    /// <param name="dy">Y 平移 (mm)。</param>
    /// <param name="dz">Z 平移 (mm)。</param>
    /// <returns>新陣列，不修改原點。</returns>
    private static PlyPoint[] Translate(PlyPoint[] points, double dx, double dy, double dz)
    {
        var ox = (float)dx;
        var oy = (float)dy;
        var oz = (float)dz;
        var copy = new PlyPoint[points.Length];
        for (var i = 0; i < points.Length; i++)
        {
            var p = points[i];
            copy[i] = new PlyPoint(p.X + ox, p.Y + oy, p.Z + oz, p.Intensity);
        }

        return copy;
    }

    static void ResolveTranslation(
        CameraPosition? camera,
        CompensationOffset? offset,
        out double dx,
        out double dy,
        out double dz)
    {
        dx = camera is null ? 0 : camera.X;
        dy = camera is null ? 0 : camera.Y;
        dz = camera is null ? 0 : camera.Z;
        if (offset is not null)
        {
            dx += offset.OffsetX;
            dy += offset.OffsetY;
            dz += offset.OffsetZ;
        }
    }

    static bool HasTranslation(double dx, double dy, double dz)
    {
        return Math.Abs(dx) > 1e-9 || Math.Abs(dy) > 1e-9 || Math.Abs(dz) > 1e-9;
    }

    /// <summary>
    /// 從 Pipeline ZIP 抽出 PLY／GLB，以及同目錄的 2D PNG／STEP。
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

        var directory = Path.GetDirectoryName(plyPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        ExtractEntriesByExtension(zipBytes, directory, ".png");
        ExtractEntriesByExtension(zipBytes, directory, ".stp");
    }

    /// <summary>
    /// 將 ZIP 內指定副檔名的項目全部寫到 <paramref name="directory"/>，檔名用 ZIP 條目名稱。
    /// </summary>
    /// <param name="zipBytes">Pipeline 產出的 ZIP。</param>
    /// <param name="directory">輸出資料夾。</param>
    /// <param name="extension">副檔名，例如 <c>.png</c>、<c>.stp</c>。</param>
    private static void ExtractEntriesByExtension(byte[] zipBytes, string directory, string extension)
    {
        using var ms = new MemoryStream(zipBytes);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        foreach (var entry in zip.Entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Name))
            {
                continue;
            }

            if (!entry.Name.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var dest = Path.Combine(directory, Path.GetFileName(entry.Name));
            using var stream = entry.Open();
            using var file = File.Create(dest);
            stream.CopyTo(file);
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
