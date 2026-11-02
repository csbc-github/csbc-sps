using System.Globalization;
using System.Text;
using System.Text.Json;
using ThreeDScan.Infrastructure.Export.Models;

namespace ThreeDScan.Infrastructure.Export;

/// <summary>兩段端對端拼接 PLY／報告匯出。</summary>
/// <remarks>由 Hank Scan3D 的 StitchExporter 搬入，邏輯不改。</remarks>
public static class StitchExporter
{
    /// <summary>寫出兩段拼接 PLY、報告與截面 CSV。</summary>
    /// <remarks>來自 Hank Scan3D 的 <c>Scan3D.Processing.Legacy.Stitching.StitchExporter</c>。</remarks>
    /// <param name="result">兩段結果。</param>
    /// <param name="sourceA">A 來源檔。</param>
    /// <param name="sourceB">B 來源檔。</param>
    /// <param name="output">輸出資料夾。</param>
    public static void Save(ButtStitchResult result, string sourceA, string sourceB, string output)
    {
        Directory.CreateDirectory(output);
        string[] names = ["stitched.ply", "all_points_transformed.ply", "part_a_aligned.ply", "part_b_aligned.ply", "stitch-report.json", "seam-profile.csv"];
        if (names.Any(name => File.Exists(Path.Combine(output, name))))
        {
            throw new IOException("拼接結果已存在，請改用新的輸出資料夾。");
        }

        string[] Properties(ScanCloud cloud)
        {
            return cloud.Header.Where(line => line.StartsWith("property ")).ToArray();
        }

        if (!Properties(result.PartA.Source).SequenceEqual(Properties(result.PartB.Source)))
        {
            throw new InvalidDataException("兩份 PLY 屬性排列不同，不能直接保留合併。");
        }

        string[] header =
        [
            "ply", "format ascii 1.0", "comment Rigid butt stitch; mm; X cross-section, Y longitudinal, Z up",
            "element vertex 0", .. Properties(result.PartA.Source), "property uchar red", "property uchar green", "property uchar blue",
            "property uchar source_scan", "property int source_point_index", "end_header",
        ];
        IEnumerable<(ScanVector Position, string ExtraColumns)> Points(StitchedPart part, int id, bool retainedOnly)
        {
            for (int i = 0; i < part.SourcePointCount; i++)
            {
                if (retainedOnly && !part.Retained[i])
                {
                    continue;
                }

                var p = part.Source.Points[i];
                string color = id == 0 ? "38 190 172" : "239 151 73";
                string attributes = (p.ExtraColumns.Length > 0 ? p.ExtraColumns + " " : "") + color + " " + id + " " + p.SourcePointIndex.ToString(CultureInfo.InvariantCulture);
                yield return (part.TransformedPoints[i], attributes);
            }
        }

        ScanPly.Write(
            Path.Combine(output, names[0]),
            header,
            Points(result.PartA, 0, true).Concat(Points(result.PartB, 1, true)),
            result.StitchedPointCount,
            "Nominal gap is an assumption for non-overlapping captures; no synthetic points");
        ScanPly.Write(
            Path.Combine(output, names[1]),
            header,
            Points(result.PartA, 0, false).Concat(Points(result.PartB, 1, false)),
            result.TotalSourcePoints,
            "Complete rigidly transformed inputs, including boundary points excluded from clean butt stitch");
        ScanPly.Write(Path.Combine(output, names[2]), header, Points(result.PartA, 0, true), result.PartA.RetainedPointCount, "Retained scan A");
        ScanPly.Write(Path.Combine(output, names[3]), header, Points(result.PartB, 1, true), result.PartB.RetainedPointCount, "Retained scan B");
        var report = new
        {
            schemaVersion = 1,
            unit = "mm",
            mode = "No-overlap butt stitch",
            coordinateSystem = "X cross-section; Y longitudinal; Z up; minimum XYZ of retained cloud zeroed",
            sourceA = new { file = Path.GetFullPath(sourceA), sha256 = result.PartA.Source.SourceSha256 },
            sourceB = new { file = Path.GetFullPath(sourceB), sha256 = result.PartB.Source.SourceSha256 },
            result.Options,
            result.Validation,
            result.TotalSourcePoints,
            result.StitchedPointCount,
            result.Bounds,
            coordinateSpanAlongYmm = result.Bounds.MaxY - result.Bounds.MinY,
            result.Assumptions,
            result.ProfileCorrection2D,
            partA = result.PartA,
            partB = result.PartB,
            provenance = "source_scan: 0=A, 1=B; source_point_index is zero-based vertex index in the referenced input PLY; intensity and extra columns preserved",
            files = names,
        };
        using (var stream = new FileStream(Path.Combine(output, names[4]), FileMode.CreateNew))
        {
            JsonSerializer.Serialize(stream, report, ExtractionJson.Options);
        }

        using var writer = new StreamWriter(Path.Combine(output, names[5]), false, new UTF8Encoding(false));
        writer.WriteLine("u_mm,a_height_mm,b_height_mm,a_minus_b_mm");
        foreach (var p in result.ValidationProfile)
        {
            writer.WriteLine(string.Join(',', new[] { p.U, p.HeightA, p.HeightB, p.DeltaMm }.Select(v => v.ToString("G17", CultureInfo.InvariantCulture))));
        }
    }
}
