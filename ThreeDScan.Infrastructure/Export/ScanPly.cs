using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ThreeDScan.Infrastructure.Export.Models;

namespace ThreeDScan.Infrastructure.Export;

/// <summary>ASCII 掃描器 PLY 讀寫。</summary>
/// <remarks>由 Hank Scan3D 的 ScanPly 搬入，邏輯不改。</remarks>
public static class ScanPly
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    /// <summary>讀取前三欄為 x y z 的 ASCII PLY。</summary>
    /// <remarks>來自 Hank Scan3D 的 <c>Scan3D.Processing.Legacy.Extraction.ScanPly</c>。</remarks>
    /// <param name="path">檔案路徑。</param>
    public static ScanCloud Read(string path)
    {
        using var reader = new StreamReader(path, Encoding.UTF8, true, 1024 * 1024);
        var header = new List<string>();
        var properties = new List<string>();
        int vertexCount = -1;
        bool ascii = false, vertices = false, ended = false;
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            line = line.Trim();
            header.Add(line);
            var fields = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length == 0)
            {
                continue;
            }

            if (fields[0] == "format")
            {
                ascii = fields.Length > 1 && fields[1] == "ascii";
            }

            if (fields[0] == "element")
            {
                vertices = fields.Length == 3 && fields[1] == "vertex";
                if (vertices)
                {
                    vertexCount = int.Parse(fields[2], Invariant);
                }
            }

            if (fields[0] == "property" && vertices)
            {
                if (fields.Length != 3)
                {
                    throw new InvalidDataException("不支援頂點 list 屬性。");
                }

                properties.Add(fields[2]);
            }

            if (line == "end_header")
            {
                ended = true;
                break;
            }
        }

        if (header.Count == 0 || header[0] != "ply" || !ended)
        {
            throw new InvalidDataException("PLY 標頭不完整。");
        }

        if (!ascii || vertexCount < 0 || properties.Count < 3 || !properties.Take(3).SequenceEqual(new[] { "x", "y", "z" }))
        {
            throw new InvalidDataException("請使用前三欄為 x y z 的 ASCII 掃描器 PLY。");
        }

        var points = new SourceScanPoint[vertexCount];
        for (int i = 0; i < points.Length; i++)
        {
            do
            {
                line = reader.ReadLine();
            }
            while (line is not null && string.IsNullOrWhiteSpace(line));
            if (line is null)
            {
                throw new InvalidDataException($"PLY 頂點不足：預期 {vertexCount}，讀到 {i}。");
            }

            var fields = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < properties.Count)
            {
                throw new InvalidDataException($"第 {i + 1} 個頂點缺少欄位。");
            }

            double x = double.Parse(fields[0], Invariant);
            double y = double.Parse(fields[1], Invariant);
            double z = double.Parse(fields[2], Invariant);
            if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(z))
            {
                throw new InvalidDataException("點雲包含無效座標。");
            }

            points[i] = new SourceScanPoint(i, new ScanVector(x, y, z), string.Join(' ', fields.Skip(3)));
        }

        using var checksumStream = File.OpenRead(path);
        string checksum = Convert.ToHexStringLower(SHA256.HashData(checksumStream));
        return new ScanCloud(header.ToArray(), points, checksum);
    }

    /// <summary>量化到小數六位。</summary>
    /// <remarks>來自 Hank Scan3D 的 <c>Scan3D.Processing.Legacy.Extraction.ScanPly</c>。</remarks>
    /// <param name="value">數值。</param>
    public static double Quantize(double value)
    {
        return double.Parse(value.ToString("F6", Invariant), Invariant);
    }

    /// <summary>量化向量到小數六位。</summary>
    /// <remarks>來自 Hank Scan3D 的 <c>Scan3D.Processing.Legacy.Extraction.ScanPly</c>。</remarks>
    /// <param name="p">向量。</param>
    public static ScanVector Quantize(ScanVector p)
    {
        return new ScanVector(Quantize(p.X), Quantize(p.Y), Quantize(p.Z));
    }

    /// <summary>寫出 ASCII PLY。失敗不會留下已發布檔。</summary>
    /// <remarks>來自 Hank Scan3D 的 <c>Scan3D.Processing.Legacy.Extraction.ScanPly</c>。</remarks>
    /// <param name="path">目標路徑。</param>
    /// <param name="sourceHeader">來源標頭。</param>
    /// <param name="points">頂點與額外欄位。</param>
    /// <param name="count">頂點數。</param>
    /// <param name="comment">註解。</param>
    /// <param name="preserveCoordinatePrecision">是否保留未量化座標。</param>
    public static void Write(
        string path,
        string[] sourceHeader,
        IEnumerable<(ScanVector Position, string ExtraColumns)> points,
        int count,
        string comment,
        bool preserveCoordinatePrecision = false)
    {
        // CreateNew preserves earlier scans/results. A failed write never leaves a published PLY.
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var writer = new StreamWriter(temporary, false, new UTF8Encoding(false), 1024 * 1024))
            {
                writer.NewLine = "\n";
                foreach (string line in sourceHeader)
                {
                    if (line.StartsWith("element vertex ", StringComparison.Ordinal))
                    {
                        writer.WriteLine($"element vertex {count}");
                    }
                    else if (line == "end_header")
                    {
                        writer.WriteLine("comment " + comment);
                        writer.WriteLine(line);
                    }
                    else
                    {
                        writer.WriteLine(line);
                    }
                }

                int written = 0;
                foreach (var (p, extra) in points)
                {
                    string Coordinate(double value)
                    {
                        return preserveCoordinatePrecision && Quantize(value) != value
                            ? value.ToString("R", Invariant)
                            : value.ToString("F6", Invariant);
                    }

                    writer.Write(Coordinate(p.X));
                    writer.Write(' ');
                    writer.Write(Coordinate(p.Y));
                    writer.Write(' ');
                    writer.Write(Coordinate(p.Z));
                    if (extra.Length > 0)
                    {
                        writer.Write(' ');
                        writer.Write(extra);
                    }

                    writer.WriteLine();
                    written++;
                }

                if (written != count)
                {
                    throw new InvalidDataException("輸出頂點數與標頭不符。");
                }
            }

            File.Move(temporary, path, false);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }
}
