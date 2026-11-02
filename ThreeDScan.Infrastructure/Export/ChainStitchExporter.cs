using System.Globalization;
using System.Text;
using System.Text.Json;
using ThreeDScan.Infrastructure.Export.Models;

namespace ThreeDScan.Infrastructure.Export;

/// <summary>多段鏈拼接 PLY／報告匯出。</summary>
/// <remarks>由 Hank Scan3D 的 ChainStitchExporter 搬入，邏輯不改。</remarks>
public static class ChainStitchExporter
{
    /// <summary>寫出拼接 PLY、各段對齊 PLY、報告與截面 CSV。</summary>
    /// <remarks>來自 Hank Scan3D 的 <c>Scan3D.Processing.Legacy.Stitching.ChainStitchExporter</c>。</remarks>
    /// <param name="result">鏈結果。</param>
    /// <param name="sources">來源檔。</param>
    /// <param name="output">輸出資料夾。</param>
    public static void Save(ChainStitchResult result, IReadOnlyList<string> sources, string output)
    {
        if (sources.Count != result.ScanCount)
        {
            throw new ArgumentException("來源檔案數與拼接段數不同。");
        }

        if (result.LegacyPair is { } pair)
        {
            StitchExporter.Save(pair, sources[0], sources[1], output);
            return;
        }

        string[] partNames = result.Parts.Select(part => $"part_{part.ScanIndex + 1:D3}_aligned.ply").ToArray();
        string[] names = ["stitched.ply", "all_points_transformed.ply", "stitch-report.json", "seam-profile.csv", .. partNames];
        if (names.Any(name => File.Exists(Path.Combine(output, name))))
        {
            throw new IOException("拼接結果已存在，請改用新的輸出資料夾。");
        }

        string[] Properties(ScanCloud cloud)
        {
            bool vertex = false;
            var properties = new List<string>();
            foreach (string line in cloud.Header)
            {
                if (line.StartsWith("element "))
                {
                    vertex = line.StartsWith("element vertex ");
                }
                else if (vertex && line.StartsWith("property "))
                {
                    properties.Add(line);
                }
            }

            return properties.ToArray();
        }

        string[] properties = Properties(result.Parts[0].Source);
        if (result.Parts.Any(part => !Properties(part.Source).SequenceEqual(properties)))
        {
            throw new InvalidDataException("各段 PLY 的頂點屬性排列必須相同，才能完整保留合併。");
        }

        var existingNames = properties.Select(line => line.Split(' ')[^1]).ToHashSet();
        if (existingNames.Contains("source_scan") || existingNames.Contains("source_point_index"))
        {
            throw new InvalidDataException("請輸入各段原始角鋼 PLY，不能把已拼接的輸出當成新掃描段。");
        }

        string[] colorNames = new[] { "red", "green", "blue" }.Where(name => !existingNames.Contains(name)).ToArray();
        string[] header =
        [
            "ply", "format ascii 1.0", "comment Ordered rigid chain butt stitch; millimetres",
            "element vertex 0", .. properties, .. colorNames.Select(name => "property uchar " + name),
            "property int source_scan", "property int source_point_index", "end_header",
        ];
        Directory.CreateDirectory(output);
        IEnumerable<(ScanVector Position, string ExtraColumns)> Points(ChainPart part, bool retainedOnly)
        {
            var color = StitchPalette.Rgb(part.ScanIndex);
            string Colors()
            {
                return string.Join(' ', colorNames.Select(name =>
                {
                    byte channel;
                    if (name == "red")
                    {
                        channel = color.R;
                    }
                    else if (name == "green")
                    {
                        channel = color.G;
                    }
                    else
                    {
                        channel = color.B;
                    }

                    return channel.ToString(CultureInfo.InvariantCulture);
                }));
            }

            string colors = Colors();
            for (int j = 0; j < part.SourcePointCount; j++)
            {
                if (retainedOnly && !part.Retained[j])
                {
                    continue;
                }

                var original = part.Source.Points[j];
                string extra = (original.ExtraColumns.Length > 0 ? original.ExtraColumns + " " : "")
                    + (colors.Length > 0 ? colors + " " : "") + part.ScanIndex.ToString(CultureInfo.InvariantCulture)
                    + " " + original.SourcePointIndex.ToString(CultureInfo.InvariantCulture);
                yield return (part.TransformedPoints[j], extra);
            }
        }

        ScanPly.Write(
            Path.Combine(output, names[0]),
            header,
            result.Parts.SelectMany(part => Points(part, true)),
            result.StitchedPointCount,
            "Adjacent original captures; configured nominal gaps; no synthetic points");
        ScanPly.Write(
            Path.Combine(output, names[1]),
            header,
            result.Parts.SelectMany(part => Points(part, false)),
            result.TotalSourcePoints,
            "Complete transformed originals including points trimmed from either end");
        foreach (var part in result.Parts)
        {
            ScanPly.Write(
                Path.Combine(output, partNames[part.ScanIndex]),
                header,
                Points(part, true),
                part.RetainedPointCount,
                $"Retained original scan {part.ScanIndex + 1}");
        }

        using (var writer = new StreamWriter(new FileStream(Path.Combine(output, names[3]), FileMode.CreateNew), new UTF8Encoding(false)))
        {
            writer.WriteLine("seam_number,left_scan_index,right_scan_index,u_mm,left_height_mm,right_height_mm,left_minus_right_mm");
            foreach (var seam in result.Seams)
            {
                foreach (var p in seam.ValidationProfile)
                {
                    writer.WriteLine(string.Join(',', new[]
                    {
                        (double)seam.SeamNumber, seam.LeftScanIndex, seam.RightScanIndex,
                        p.U, p.HeightA, p.HeightB, p.DeltaMm,
                    }.Select(value => value.ToString("G17", CultureInfo.InvariantCulture))));
                }
            }
        }

        var report = new
        {
            schemaVersion = 2,
            unit = "mm",
            mode = "Ordered no-overlap chain butt stitch",
            coordinateSystem = "First seam frame; all retained minimum XYZ zeroed; seam planes may rotate with rigid segment poses",
            sources = sources.Select((file, i) => new { scanIndex = i, file = Path.GetFullPath(file), sha256 = result.Parts[i].Source.SourceSha256 }).ToArray(),
            result.ScanCount,
            result.SeamCount,
            result.Passed,
            result.Options,
            result.TotalSourcePoints,
            result.StitchedPointCount,
            result.Bounds,
            coordinateSpanAlongYmm = result.Bounds.MaxY - result.Bounds.MinY,
            result.PhysicalOverallLengthVerified,
            result.Assumptions,
            result.Parts,
            result.Seams,
            provenance = "source_scan is a zero-based int index into sources; source_point_index is the original zero-based vertex index. Original extra columns are preserved. Seam end poses/indices use local pair coordinates; LocalToGlobalPose maps them into final coordinates.",
            files = names,
        };
        using var stream = new FileStream(Path.Combine(output, names[2]), FileMode.CreateNew);
        JsonSerializer.Serialize(stream, report, ExtractionJson.Options);
    }
}
