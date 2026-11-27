using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ThreeDScan.Infrastructure.Export.Models;

namespace ThreeDScan.Infrastructure.Export;

/// <summary>ASCII 掃描器 PLY 讀寫。</summary>
/// <remarks>保留數值、額外欄位及原始位元組雜湊；以 span 解析與格式化減少逐點配置。</remarks>
public static class ScanPly
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    // A raw height capture commonly has only one extra byte-intensity column.
    // Share only the exact canonical spellings; "001" and other attributes
    // keep their original token text.
    private static readonly string[] SmallExtraValues = Enumerable.Range(0, 256)
        .Select(value => value.ToString(Invariant)).ToArray();

    /// <summary>讀取前三欄為 x y z 的 ASCII PLY。</summary>
    /// <remarks>來自 Hank Scan3D 的 <c>Scan3D.Processing.Legacy.Extraction.ScanPly</c>。</remarks>
    /// <param name="path">檔案路徑。</param>
    public static ScanCloud Read(string path)
    {
        using var checksum = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            1024 * 1024, FileOptions.SequentialScan);
        using var hashed = new HashingReadStream(source, checksum);
        using var reader = new StreamReader(hashed, Encoding.UTF8, true, 1024 * 1024, leaveOpen: true);
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

            points[i] = ParsePoint(line, properties.Count, i);
        }

        // StreamReader may have read ahead past the declared vertices. Those
        // original bytes are already hashed; drain only bytes not yet read so
        // comments/trailing elements/BOM/line endings remain in the full SHA.
        hashed.CopyTo(Stream.Null);
        string sourceChecksum = Convert.ToHexStringLower(checksum.GetHashAndReset());
        return new ScanCloud(header.ToArray(), points, sourceChecksum);
    }

    /// <summary>量化到小數六位。</summary>
    /// <remarks>來自 Hank Scan3D 的 <c>Scan3D.Processing.Legacy.Extraction.ScanPly</c>。</remarks>
    /// <param name="value">數值。</param>
    public static double Quantize(double value)
    {
        // Scanner-scale fixed-point values fit here. Extreme IEEE values use
        // the original formatter rather than clearing a large stack buffer on
        // every coordinate of a multi-million-point capture.
        Span<char> buffer = stackalloc char[32];
        if (!value.TryFormat(buffer, out int written, "F6", Invariant))
            return double.Parse(value.ToString("F6", Invariant), Invariant);
        return double.Parse(buffer[..written], Invariant);
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
                Span<char> coordinateBuffer = stackalloc char[384];
                foreach (var (p, extra) in points)
                {
                    WriteCoordinate(writer, p.X, preserveCoordinatePrecision, coordinateBuffer);
                    writer.Write(' ');
                    WriteCoordinate(writer, p.Y, preserveCoordinatePrecision, coordinateBuffer);
                    writer.Write(' ');
                    WriteCoordinate(writer, p.Z, preserveCoordinatePrecision, coordinateBuffer);
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

    private static SourceScanPoint ParsePoint(string line, int requiredFields, int index)
    {
        ReadOnlySpan<char> text = line.AsSpan();
        Span<Range> coordinates = stackalloc Range[3];
        int cursor = 0, count = 0, extraStart = -1, previousEnd = 0, extraEnd = 0;
        int normalizedExtraLength = 0;
        bool canonicalExtraSeparators = true;
        while (cursor < text.Length)
        {
            while (cursor < text.Length && char.IsWhiteSpace(text[cursor])) cursor++;
            if (cursor == text.Length) break;
            int start = cursor;
            while (cursor < text.Length && !char.IsWhiteSpace(text[cursor])) cursor++;
            int length = cursor - start;
            if (count < 3)
            {
                coordinates[count] = start..cursor;
            }
            else
            {
                if (count == 3) extraStart = start;
                else
                {
                    normalizedExtraLength++;
                    if (start != previousEnd + 1 || text[previousEnd] != ' ')
                        canonicalExtraSeparators = false;
                }
                normalizedExtraLength += length;
                extraEnd = cursor;
            }
            previousEnd = cursor;
            count++;
        }
        // Keep the original error ordering: a short record is rejected before
        // any numeric token is parsed.
        if (count < requiredFields)
            throw new InvalidDataException($"第 {index + 1} 個頂點缺少欄位。");
        double x = double.Parse(text[coordinates[0]], Invariant);
        double y = double.Parse(text[coordinates[1]], Invariant);
        double z = double.Parse(text[coordinates[2]], Invariant);
        if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(z))
            throw new InvalidDataException("點雲包含無效座標。");

        string extra = "";
        if (extraStart >= 0)
        {
            ReadOnlySpan<char> extraText = text[extraStart..extraEnd];
            if (count == 4 && TrySharedExtra(extraText, out var shared)) extra = shared;
            else if (canonicalExtraSeparators) extra = extraText.ToString();
            else extra = string.Create(normalizedExtraLength, (line, extraStart, extraEnd),
                static (destination, state) =>
                {
                    var sourceText = state.line.AsSpan(state.extraStart, state.extraEnd - state.extraStart);
                    int read = 0, write = 0;
                    while (read < sourceText.Length)
                    {
                        while (read < sourceText.Length && char.IsWhiteSpace(sourceText[read])) read++;
                        if (read == sourceText.Length) break;
                        if (write > 0) destination[write++] = ' ';
                        while (read < sourceText.Length && !char.IsWhiteSpace(sourceText[read]))
                            destination[write++] = sourceText[read++];
                    }
                });
        }
        return new SourceScanPoint(index, new ScanVector(x, y, z), extra);
    }

    private static bool TrySharedExtra(ReadOnlySpan<char> token, out string value)
    {
        value = "";
        if (token.Length is < 1 or > 3 || token.Length > 1 && token[0] == '0') return false;
        int number = 0;
        foreach (char c in token)
        {
            if (c is < '0' or > '9') return false;
            number = number * 10 + c - '0';
        }
        if (number >= SmallExtraValues.Length) return false;
        value = SmallExtraValues[number];
        return true;
    }

    private static void WriteCoordinate(StreamWriter writer, double value, bool preserve, Span<char> buffer)
    {
        if (!value.TryFormat(buffer, out int written, "F6", Invariant))
        {
            writer.Write(preserve && Quantize(value) != value ? value.ToString("R", Invariant) : value.ToString("F6", Invariant));
            return;
        }
        if (preserve && double.Parse(buffer[..written], Invariant) != value)
            value.TryFormat(buffer, out written, "R", Invariant);
        writer.Write(buffer[..written]);
    }

    private sealed class HashingReadStream(Stream source, IncrementalHash checksum) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => source.Length;
        public override long Position { get => source.Position; set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count)
        {
            int read = source.Read(buffer, offset, count);
            checksum.AppendData(buffer, offset, read);
            return read;
        }
        public override int Read(Span<byte> buffer)
        {
            int read = source.Read(buffer);
            checksum.AppendData(buffer[..read]);
            return read;
        }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
