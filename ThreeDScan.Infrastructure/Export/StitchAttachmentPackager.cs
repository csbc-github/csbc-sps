using System.IO.Compression;
using System.Text.Json;
using ThreeDScan.Domain.Models;

namespace ThreeDScan.Infrastructure.Export;

/// <summary>面掃描完成後打包附件 ZIP（Hank ArtifactService.BuildAttachments）。</summary>
public static class StitchAttachmentPackager
{
    /// <summary>
    /// 建立 attachments.zip；STEP／glTF／GLB 缺任一時回傳 null（任務仍可 Completed）。
    /// </summary>
    /// <param name="taskId">任務識別。</param>
    /// <param name="result">拼接結果。</param>
    /// <param name="ct">取消權杖。</param>
    /// <returns>ZIP 絕對路徑，或 null。</returns>
    public static async Task<string?> BuildAsync(string taskId, StitchProcessingResult result, CancellationToken ct)
    {
        if (result.StepPath is null || result.GltfPath is null || result.GlbPath is null)
        {
            return null;
        }

        var files = new List<string> { result.StepPath, result.GltfPath, result.GlbPath, result.ReportPath };
        if (result.ModelReportPath is not null)
        {
            files.Add(result.ModelReportPath);
        }

        var directory = Path.GetDirectoryName(result.StitchedPlyPath)!;
        var cloud = await Task.Run(() => ScanPly.Read(result.StitchedPlyPath), ct).ConfigureAwait(false);
        foreach (var view in new[] { "top", "front", "side" })
        {
            ct.ThrowIfCancellationRequested();
            var png = Path.Combine(directory, view + ".png");
            await Task.Run(() => StitchPlyPreviewRenderer.Render(cloud, png, view), ct).ConfigureAwait(false);
            files.Add(png);
        }

        var manifest = Path.Combine(directory, "attachments-manifest.json");
        var manifestPayload = new
        {
            taskId,
            thicknessMm = result.ThicknessMm,
            absoluteCoordinatesCalibrated = result.AbsoluteCoordinatesCalibrated,
            unit = "STEP mm; glTF/GLB metres, Y-up",
            files = files.Select(Path.GetFileName).ToArray(),
            warnings = result.Warnings,
        };
        await File.WriteAllTextAsync(manifest, JsonSerializer.Serialize(manifestPayload, JsonDefaults.Options), ct)
            .ConfigureAwait(false);
        files.Add(manifest);

        var zipPath = Path.Combine(directory, "attachments.zip");
        await Task.Run(() =>
        {
            using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
            foreach (var file in files)
            {
                ct.ThrowIfCancellationRequested();
                zip.CreateEntryFromFile(file, Path.GetFileName(file), CompressionLevel.Fastest);
            }
        }, ct).ConfigureAwait(false);
        return zipPath;
    }
}
