using System.Globalization;

namespace ThreeDScan.Infrastructure.Export;

/// <summary>讀取 ASCII 掃描器 PLY，不過濾、不重排、不轉換頂點。</summary>
/// <remarks>由 Hank Scan3D 的 ScannerPlyReader 搬入，邏輯不改。</remarks>
public static class ScannerPlyReader
{
    /// <summary>由檔案讀取 XYZ（float，與 JS Float32Array 相同）。</summary>
    /// <remarks>來自 Hank PlyProcessor.Holes 的 <c>PlyProcessor.Holes.ScannerPlyReader</c>。</remarks>
    /// <param name="path">路徑。</param>
    public static float[] Read(string path)
    {
        using var stream = File.OpenRead(path);
        return Read(stream);
    }

    /// <summary>由串流讀取 XYZ。</summary>
    /// <remarks>來自 Hank PlyProcessor.Holes 的 <c>PlyProcessor.Holes.ScannerPlyReader</c>。</remarks>
    /// <param name="stream">串流。</param>
    public static float[] Read(Stream stream)
    {
        using var reader = new StreamReader(stream, leaveOpen: true);
        if (reader.ReadLine()?.Trim() != "ply")
        {
            throw new InvalidDataException("檔案不是 PLY 格式。");
        }

        int count = -1;
        bool ascii = false, inVertex = false, ended = false;
        var properties = new List<string>();
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                continue;
            }

            if (parts[0] == "format")
            {
                ascii = parts.Length >= 2 && parts[1] == "ascii";
            }

            if (parts[0] == "element")
            {
                inVertex = parts.Length == 3 && parts[1] == "vertex";
                if (inVertex && !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out count))
                {
                    throw new InvalidDataException("PLY 頂點數無效。");
                }
            }

            if (parts[0] == "property" && inVertex)
            {
                if (parts.Length != 3 || parts[1] == "list")
                {
                    throw new InvalidDataException("不支援頂點的 list 屬性。");
                }

                properties.Add(parts[2]);
            }

            if (parts[0] == "end_header")
            {
                ended = true;
                break;
            }
        }

        if (!ended)
        {
            throw new InvalidDataException("PLY 缺少 end_header。");
        }

        if (!ascii)
        {
            throw new InvalidDataException("請使用 ASCII 格式的掃描器 PLY 檔案。");
        }

        int xi = properties.IndexOf("x"), yi = properties.IndexOf("y"), zi = properties.IndexOf("z");
        if (count < 0 || xi < 0 || yi < 0 || zi < 0)
        {
            throw new InvalidDataException("PLY 缺少 vertex 或 XYZ 屬性。");
        }

        var positions = new float[checked(count * 3)];
        for (int i = 0; i < count; i++)
        {
            do
            {
                line = reader.ReadLine();
            }
            while (line is not null && string.IsNullOrWhiteSpace(line));
            if (line is null)
            {
                throw new InvalidDataException($"PLY 頂點不足：預期 {count}，讀到 {i}。");
            }

            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < properties.Count)
            {
                throw new InvalidDataException($"PLY 第 {i + 1} 個頂點缺少欄位。");
            }

            int[] columns = [xi, yi, zi];
            for (int j = 0; j < 3; j++)
            {
                // Parse as double then cast, exactly like JS Number -> Float32Array.
                if (!double.TryParse(parts[columns[j]], NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                    || !double.IsFinite(value) || !float.IsFinite((float)value))
                {
                    throw new InvalidDataException($"PLY 第 {i + 1} 個頂點包含無效座標。");
                }

                positions[i * 3 + j] = (float)value;
            }
        }

        return positions;
    }
}
