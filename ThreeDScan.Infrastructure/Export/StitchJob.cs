using System.Text.Json;
using ThreeDScan.Infrastructure.Export.Models;

namespace ThreeDScan.Infrastructure.Export;

/// <summary>有來源譜系時先恢復外緣量測點，再拼接並匯出。</summary>
/// <remarks>由 Hank Scan3D 的 StitchJob 搬入，邏輯不改。</remarks>
public static class StitchJob
{
    /// <summary>準備輸入、執行鏈拼接並寫出結果。</summary>
    /// <remarks>來自 Hank Scan3D 的 <c>Scan3D.Processing.Legacy.Stitching.StitchJob</c>。</remarks>
    /// <param name="files">PLY 路徑。</param>
    /// <param name="output">輸出資料夾。</param>
    /// <param name="options">拼接參數。</param>
    /// <param name="recoverMeasuredEdges">是否恢復外緣量測點。</param>
    /// <param name="log">進度紀錄。</param>
    public static ChainStitchResult Run(
        IReadOnlyList<string> files,
        string output,
        ButtStitchOptions? options = null,
        bool recoverMeasuredEdges = true,
        Action<string>? log = null)
    {
        if (files.Count < 2)
        {
            throw new ArgumentException("請至少選擇兩份角鋼 PLY。");
        }

        string[] paths = files.Select(Path.GetFullPath).ToArray();
        if (paths.Distinct(StringComparer.OrdinalIgnoreCase).Count() != paths.Length)
        {
            throw new ArgumentException("同一份 PLY 不能重複加入。");
        }

        output = Path.GetFullPath(output);
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
        {
            throw new IOException("結果資料夾不是空的，請選擇新的資料夾。");
        }

        var prepared = new PreparedStitchInput[paths.Length];
        for (int i = 0; i < paths.Length; i++)
        {
            log?.Invoke($"檢查第 {i + 1}/{paths.Length} 段原始量測外緣…");
            prepared[i] = Prepare(paths[i], i, output, recoverMeasuredEdges);
            log?.Invoke($"第 {i + 1} 段：{prepared[i].Message}");
        }

        if (prepared.Any(input => input.AddedMeasuredPointCount > 0))
        {
            for (int i = 0; i < prepared.Length; i++)
            {
                if (prepared[i].EffectiveFile != prepared[i].OriginalFile)
                {
                    continue;
                }

                var cloud = ScanPly.Read(prepared[i].OriginalFile);
                var properties = cloud.Header.Where(line => line.StartsWith("property ")).ToArray();
                if (properties.Any(line => line.EndsWith(" input_point_index")) && properties.Any(line => line.EndsWith(" raw_point_index")))
                {
                    continue;
                }

                if (properties.Any(line => line.EndsWith(" input_point_index") || line.EndsWith(" raw_point_index")))
                {
                    throw new InvalidDataException("來源追蹤欄位不完整，請使用原始角鋼輸入。");
                }

                string inputDirectory = Path.Combine(output, "prepared-inputs");
                Directory.CreateDirectory(inputDirectory);
                string normalizedFile = Path.Combine(inputDirectory, $"scan_{i + 1:D3}.ply");
                string[] header = ["ply", "format ascii 1.0", "element vertex 0", .. properties, "property int input_point_index", "property int raw_point_index", "end_header"];
                ScanPly.Write(
                    normalizedFile,
                    header,
                    cloud.Points.Select(point => (point.Position,
                        (point.ExtraColumns.Length > 0 ? point.ExtraColumns + " " : "") + point.SourcePointIndex + " -1")),
                    cloud.Points.Length,
                    "Original selected points; source fields normalized for mixed recovered/unrecovered captures");
                prepared[i] = new PreparedStitchInput(
                    prepared[i].OriginalFile,
                    normalizedFile,
                    prepared[i].OriginalSha256,
                    prepared[i].OriginalPointCount,
                    prepared[i].PreparedPointCount,
                    prepared[i].AddedMeasuredPointCount,
                    prepared[i].RawFile,
                    prepared[i].RawSha256,
                    prepared[i].RawToInputMatrix,
                    prepared[i].Message);
            }
        }

        string[] effective = prepared.Select(input => input.EffectiveFile).ToArray();
        var result = ChainStitcher.Run(effective, options, log);
        ChainStitchExporter.Save(result, effective, output);
        var report = new
        {
            schemaVersion = 1,
            recoverMeasuredEdges,
            originalInputFiles = paths,
            effectiveInputFiles = effective,
            inputs = prepared,
            result.Passed,
            result.ScanCount,
            result.SeamCount,
            method = "Extend measured outer edges from matching original raw scans using local scanner-row wing fits and contiguous measured samples. Never fabricate, scale, interpolate or fill interior holes.",
            provenance = "For prepared inputs, input_point_index maps to the original selected PLY (-1 for an added measurement); raw_point_index maps to the original raw PLY. source_point_index in stitched outputs maps to the effective input PLY.",
            coordinateQuantizationMm = .000001,
        };
        using var stream = new FileStream(Path.Combine(output, "job-report.json"), FileMode.CreateNew);
        JsonSerializer.Serialize(stream, report, ExtractionJson.Options);
        return result;
    }

    private sealed class Row
    {
        public Row(SourceScanPoint[] seeds, List<(int Index, ScanVector Position)> candidates)
        {
            Seeds = seeds;
            Candidates = candidates;
        }

        public SourceScanPoint[] Seeds { get; }

        public List<(int Index, ScanVector Position)> Candidates { get; }
    }

    private static PreparedStitchInput Prepare(string file, int number, string output, bool recover)
    {
        var seed = ScanPly.Read(file);
        PreparedStitchInput Unchanged(string message)
        {
            return new PreparedStitchInput(
                file,
                file,
                seed.SourceSha256!,
                seed.Points.Length,
                seed.Points.Length,
                0,
                null,
                null,
                null,
                message);
        }

        if (!recover)
        {
            return Unchanged("使用選取的點雲；未啟用原始量測外緣恢復。");
        }

        string directory = Path.GetDirectoryName(file)!;
        foreach (string metadataPath in Directory.EnumerateFiles(directory, "*_processing.json"))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(metadataPath));
            var metadata = document.RootElement;
            if (!metadata.TryGetProperty("objects", out var objects))
            {
                continue;
            }

            foreach (var component in objects.EnumerateArray())
            {
                bool IsFile(string key)
                {
                    return component.TryGetProperty(key, out var property) && property.ValueKind == JsonValueKind.String
                        && string.Equals(property.GetString(), Path.GetFileName(file), StringComparison.OrdinalIgnoreCase);
                }

                bool calibrated = IsFile("calibratedFile");
                if (!calibrated && !IsFile("scannerFile"))
                {
                    continue;
                }

                string rawFile = Path.Combine(directory, Path.GetFileName(metadata.GetProperty("source").GetString()!));
                if (!File.Exists(rawFile))
                {
                    return Unchanged("找不到配套的原始掃描；保留輸入並執行完整截面檢查。");
                }

                var raw = ScanPly.Read(rawFile);
                if (!string.Equals(raw.SourceSha256, metadata.GetProperty("sourceSha256").GetString(), StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("原始掃描與前處理報告的 SHA-256 不符，無法回查量測點。");
                }

                double[][] matrix = calibrated
                    ? metadata.GetProperty("calibration").GetProperty("rawToCalibratedMatrix")
                        .EnumerateArray().Select(row => row.EnumerateArray().Select(value => value.GetDouble()).ToArray()).ToArray()
                    : [[1, 0, 0, 0], [0, 1, 0, 0], [0, 0, 1, 0], [0, 0, 0, 1]];
                if (matrix.Length != 4 || matrix.Any(row => row.Length != 4 || row.Any(value => !double.IsFinite(value))))
                {
                    throw new InvalidDataException("前處理座標矩陣無效。");
                }

                ScanVector Transform(ScanVector p)
                {
                    return new ScanVector(
                        matrix[0][0] * p.X + matrix[0][1] * p.Y + matrix[0][2] * p.Z + matrix[0][3],
                        matrix[1][0] * p.X + matrix[1][1] * p.Y + matrix[1][2] * p.Z + matrix[1][3],
                        matrix[2][0] * p.X + matrix[2][1] * p.Y + matrix[2][2] * p.Z + matrix[2][3]);
                }

                int[] rawIndices = component.GetProperty("sourcePointIndices").EnumerateArray().Select(value => value.GetInt32()).ToArray();
                if (rawIndices.Length != seed.Points.Length || rawIndices.Distinct().Count() != rawIndices.Length
                    || rawIndices.Any(index => index < 0 || index >= raw.Points.Length))
                {
                    throw new InvalidDataException("前處理來源點索引不符。");
                }

                string[] Properties(ScanCloud cloud)
                {
                    return cloud.Header.Where(line => line.StartsWith("property ")).ToArray();
                }

                if (!Properties(raw).SequenceEqual(Properties(seed)))
                {
                    throw new InvalidDataException("原始與取出點雲的頂點欄位不同，不能完整回查。");
                }

                var kept = new Dictionary<int, (ScanVector Position, string Extra, int SeedIndex)>();
                for (int i = 0; i < rawIndices.Length; i++)
                {
                    var original = seed.Points[i];
                    if ((Transform(raw.Points[rawIndices[i]].Position) - original.Position).Length > .000002)
                    {
                        throw new InvalidDataException("前處理矩陣無法重現選取的點雲，不能自動恢復外緣。");
                    }

                    kept.Add(rawIndices[i], (ScanPly.Quantize(original.Position), original.ExtraColumns, i));
                }

                var rows = seed.Points.GroupBy(point => ScanPly.Quantize(point.Position.Y))
                    .ToDictionary(group => group.Key, group => new Row(group.ToArray(), []));
                var limits = rows.ToDictionary(row => row.Key, row => (Min: row.Value.Seeds.Min(p => p.Position.X) - 25,
                    Max: row.Value.Seeds.Max(p => p.Position.X) + 25));
                for (int i = 0; i < raw.Points.Length; i++)
                {
                    var aligned = Transform(raw.Points[i].Position);
                    double y = ScanPly.Quantize(aligned.Y);
                    if (!rows.TryGetValue(y, out var row))
                    {
                        continue;
                    }

                    var range = limits[y];
                    if (aligned.X < range.Min || aligned.X > range.Max)
                    {
                        continue;
                    }

                    row.Candidates.Add((i, ScanPly.Quantize(aligned)));
                }

                foreach (var row in rows.Values)
                {
                    foreach (bool upper in new[] { false, true })
                    {
                        double edge = upper ? row.Seeds.Max(p => p.Position.X) : row.Seeds.Min(p => p.Position.X);
                        var training = row.Seeds.Where(p => upper ? p.Position.X >= edge - 12 : p.Position.X <= edge + 12).Select(p => p.Position).ToArray();
                        if (training.Length < 15 || training.Max(p => p.X) - training.Min(p => p.X) < 4)
                        {
                            continue;
                        }

                        double meanX = training.Average(p => p.X), meanZ = training.Average(p => p.Z);
                        double slope = training.Sum(p => (p.X - meanX) * (p.Z - meanZ)) / training.Sum(p => (p.X - meanX) * (p.X - meanX));
                        if (Math.Abs(slope) < .2 || (upper ? slope > 0 : slope < 0))
                        {
                            continue;
                        }

                        double offset = meanZ - slope * meanX;
                        var candidates = row.Candidates.Where(p => Math.Abs(p.Position.X - edge) <= 25
                            && (upper ? p.Position.X > edge : p.Position.X < edge)
                            && Math.Abs(p.Position.Z - slope * p.Position.X - offset) / Math.Sqrt(1 + slope * slope) <= .5);
                        var ordered = upper ? candidates.OrderBy(p => p.Position.X) : candidates.OrderByDescending(p => p.Position.X);
                        foreach (var candidate in ordered)
                        {
                            if (Math.Abs(candidate.Position.X - edge) > .8)
                            {
                                break;
                            }

                            kept.TryAdd(candidate.Index, (candidate.Position, raw.Points[candidate.Index].ExtraColumns, -1));
                            edge = candidate.Position.X;
                        }
                    }
                }

                int added = kept.Count - seed.Points.Length;
                if (added == 0)
                {
                    return Unchanged("未找到可連續恢復的外緣量測點；執行完整截面檢查。");
                }

                string inputDirectory = Path.Combine(output, "prepared-inputs");
                Directory.CreateDirectory(inputDirectory);
                string effective = Path.Combine(inputDirectory, $"scan_{number + 1:D3}.ply");
                var properties = Properties(seed);
                if (properties.Any(line => line.EndsWith(" input_point_index") || line.EndsWith(" raw_point_index")))
                {
                    throw new InvalidDataException("請使用原始取出的角鋼 PLY，這份資料已包含恢復來源欄位。");
                }

                string[] header = ["ply", "format ascii 1.0", "element vertex 0", .. properties,
                    "property int input_point_index", "property int raw_point_index", "end_header"];
                ScanPly.Write(
                    effective,
                    header,
                    kept.OrderBy(pair => pair.Key).Select(pair =>
                        (pair.Value.Position, (pair.Value.Extra.Length > 0 ? pair.Value.Extra + " " : "") + pair.Value.SeedIndex + " " + pair.Key)),
                    kept.Count,
                    "Real original raw measurements; no interpolated or synthesized points");
                return new PreparedStitchInput(
                    file,
                    effective,
                    seed.SourceSha256!,
                    seed.Points.Length,
                    kept.Count,
                    added,
                    rawFile,
                    raw.SourceSha256,
                    matrix,
                    $"找回 {added:N0} 個原始外緣量測點；沒有補造點。");
            }
        }

        return Unchanged("沒有配套的前處理來源報告；保留輸入並執行完整截面檢查。");
    }
}
