using System.Text.Json;
using ThreeDScan.Infrastructure.Export.Models;

namespace ThreeDScan.Infrastructure.Export;

public enum HoleExportMode { Centers, Edges, Boundary }

public sealed record HoleExportFiles(string CentersJsonPath, string EdgesJsonPath, string BoundaryJsonPath);

/// <summary>與獨立孔洞程式相同的三種 schema v2 匯出，保留全部完整及不完整開口。</summary>
public static class HoleExporter
{
    public static string ToJson(HoleDetectionResult result, string sourcePlyPath, HoleExportMode mode,
        int? expectedHoleCount = 3, int[]? sourcePointIndices = null)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (sourcePointIndices is not null && (sourcePointIndices.Length != result.PointCount
            || sourcePointIndices.Any(i => i < 0)))
            throw new ArgumentException("來源 PLY 索引數量必須與分析點數一致且不得為負數。", nameof(sourcePointIndices));
        BoundaryPoint Map(BoundaryPoint p) => sourcePointIndices is null ? p : new(
            sourcePointIndices[p.SourcePointIndex], p.XMm, p.YMm, p.ZMm, p.UMm, p.VMm, p.PlaneDistanceMm);

        var holes = result.AllOpenings().Select(h =>
        {
            var item = new Dictionary<string, object?>
            {
                ["id"] = h.Id, ["faceId"] = h.FaceId, ["status"] = h.Status, ["reason"] = h.Reason,
                ["centerEstimate"] = new { xMm = h.X, yMm = h.Y, zMm = h.Z, uMm = h.U, vMm = h.V },
                ["boundaryBox"] = h.BoundaryBox, ["closure"] = h.Closure, ["scanlines"] = h.Scanlines,
                ["edgePointCount"] = h.EdgePointCount, ["boundarySource"] = h.BoundarySource,
                ["openingWidthRangeMm"] = h.OpeningWidthRangeMm,
            };
            if (mode == HoleExportMode.Boundary) item["boundaryPoints"] = h.PerimeterPoints.Select(Map).ToArray();
            if (mode == HoleExportMode.Edges) item["edgePairs"] = h.EdgePoints
                .Select(e => new HoleEdgePair(Map(e.Point1), Map(e.Point2), e.ScanYmm)).ToArray();
            return item;
        }).ToArray();
        var document = new Dictionary<string, object?>
        {
            ["schemaVersion"] = 2, ["source"] = sourcePlyPath, ["unit"] = "mm",
            ["coordinateSystem"] = "Source PLY XYZ; per-face U/V frame",
            ["method"] = result.Method, ["scannerPitch"] = result.ScannerPitch,
            ["expectedHoleCount"] = expectedHoleCount == 0 ? null : expectedHoleCount,
            ["detectedOpeningCount"] = result.Holes.Length + result.PartialHoles.Length,
            ["closedHoleCount"] = result.Holes.Length, ["partialHoleCount"] = result.PartialHoles.Length,
            ["faces"] = result.Faces, ["limitations"] = result.Limitations, ["holes"] = holes,
        };
        if (sourcePointIndices is not null) document["sourcePointIndexBasis"] = "SourcePlyVertexIndex";
        if (result.Scope is not null) document["holeScope"] = result.Scope;
        return JsonSerializer.Serialize(document, HoleJson.Options);
    }

    public static HoleExportFiles Save(HoleAnalysisResult analysis, string outputDirectory, string sourcePlyPath,
        int? expectedHoleCount = 3)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        Directory.CreateDirectory(outputDirectory);
        var files = new HoleExportFiles(
            Path.Combine(outputDirectory, FileName(sourcePlyPath, HoleExportMode.Centers)),
            Path.Combine(outputDirectory, FileName(sourcePlyPath, HoleExportMode.Edges)),
            Path.Combine(outputDirectory, FileName(sourcePlyPath, HoleExportMode.Boundary)));
        var exports = new[]
        {
            (files.CentersJsonPath, HoleExportMode.Centers),
            (files.EdgesJsonPath, HoleExportMode.Edges),
            (files.BoundaryJsonPath, HoleExportMode.Boundary),
        };
        if (exports.Any(e => File.Exists(e.Item1))) throw new IOException("孔洞匯出已存在，請使用新的輸出資料夾。");
        foreach (var (path, mode) in exports)
            File.WriteAllText(path, ToJson(analysis.Detection, Path.GetFileName(sourcePlyPath), mode,
                expectedHoleCount, analysis.AnalysisSourcePointIndices));
        return files;
    }

    public static string FileName(string sourcePlyPath, HoleExportMode mode) =>
        $"{Path.GetFileNameWithoutExtension(sourcePlyPath)}_hole_{mode.ToString().ToLowerInvariant()}.json";
}
