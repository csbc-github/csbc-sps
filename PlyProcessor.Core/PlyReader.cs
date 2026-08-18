using System.Text;

namespace PlyProcessor.Core;

public static class PlyReader
{
    public static (PlyPoint[] Points, string HeaderText) Parse(Stream stream)
    {
        using var reader = new StreamReader(stream, Encoding.Latin1, leaveOpen: true);
        var header = new StringBuilder();
        string? line;
        int vertexCount = 0;

        while ((line = reader.ReadLine()) != null)
        {
            header.AppendLine(line);
            if (line.StartsWith("element vertex"))
                int.TryParse(line.Split(' ')[^1], out vertexCount);
            if (line == "end_header") break;
        }

        var points = new PlyPoint[vertexCount];
        int count = 0;
        while (count < vertexCount && (line = reader.ReadLine()) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var parts = line.Split(' ');
            if (parts.Length < 3) continue;
            if (!float.TryParse(parts[0], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float x)) continue;
            if (!float.TryParse(parts[1], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float y)) continue;
            if (!float.TryParse(parts[2], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float z)) continue;
            byte intensity = parts.Length > 3 && byte.TryParse(parts[3], out byte iv) ? iv : (byte)180;
            points[count++] = new PlyPoint(x, y, z, intensity);
        }

        if (count < vertexCount)
            points = points[..count];

        return (points, header.ToString());
    }
}
