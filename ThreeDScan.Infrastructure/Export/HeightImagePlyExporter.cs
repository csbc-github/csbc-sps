using System.Globalization;
using System.Text;
using Keyence.LjDevCommon;

namespace ThreeDScan.Infrastructure.Export;

/// <summary>
/// 將 <see cref="LHeightImage"/> 或已換算的點陣列寫成 PLY。
/// KEYENCE SDK 無官方 PLY API；換算：
///   x = ImagePosition.X + i * CoefficientX
///   y = ImagePosition.Y + j * CoefficientY
///   z = ImagePosition.Z + raw * CoefficientZ
/// raw == 0 視為無效點略過。
/// </summary>
public static class HeightImagePlyExporter
{
    /// <summary>由高度圖蒐集有效點（掃描器座標系，mm）。</summary>
    public static List<(float X, float Y, float Z)> CollectPoints(LHeightImage heightImage)
    {
        var source = heightImage.Source
            ?? throw new InvalidOperationException("Height image Source is null.");
        if (source.Length == 0)
            throw new InvalidOperationException("Height image is empty.");

        var xSize = heightImage.XSize;
        var ySize = heightImage.YSize;
        var ox = heightImage.ImagePosition.X;
        var oy = heightImage.ImagePosition.Y;
        var oz = heightImage.ImagePosition.Z;
        var cx = heightImage.CoefficientX;
        var cy = heightImage.CoefficientY;
        var cz = heightImage.CoefficientZ;

        var points = new List<(float X, float Y, float Z)>(source.Length / 4);
        for (var j = 0; j < ySize; j++)
        {
            var rowOffset = j * xSize;
            var yVal = (float)(oy + j * cy);
            for (var i = 0; i < xSize; i++)
            {
                var raw = source[rowOffset + i];
                if (raw == 0) continue;
                points.Add(((float)(ox + i * cx), yVal, (float)(oz + raw * cz)));
            }
        }

        if (points.Count == 0)
            throw new InvalidOperationException("Height image has no valid points (all raw == 0).");

        return points;
    }

    /// <summary>寫出 ASCII PLY（供現有 WPF 點雲載入使用）。回傳點數。</summary>
    public static int SaveAsAsciiPly(IReadOnlyList<(float X, float Y, float Z)> points, string filePath)
    {
        if (points.Count == 0)
            throw new InvalidOperationException("No points to write.");

        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        using var writer = new StreamWriter(filePath, false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.NewLine = "\n";
        writer.WriteLine("ply");
        writer.WriteLine("format ascii 1.0");
        writer.WriteLine("comment ThreeDScan HeightImagePlyExporter — units mm (scanner frame)");
        writer.WriteLine($"element vertex {points.Count}");
        writer.WriteLine("property float x");
        writer.WriteLine("property float y");
        writer.WriteLine("property float z");
        writer.WriteLine("end_header");

        var inv = CultureInfo.InvariantCulture;
        foreach (var (x, y, z) in points)
            writer.WriteLine(string.Format(inv, "{0:G9} {1:G9} {2:G9}", x, y, z));

        return points.Count;
    }

    /// <summary>高度圖 → binary PLY（完整場景，未過濾）。</summary>
    public static int SaveAsBinaryPly(LHeightImage heightImage, string filePath)
        => SaveAsBinaryPly(CollectPoints(heightImage), filePath);

    /// <summary>高度圖 → ASCII PLY（完整場景，未過濾）。</summary>
    public static int SaveAsAsciiPly(LHeightImage heightImage, string filePath)
        => SaveAsAsciiPly(CollectPoints(heightImage), filePath);

    /// <summary>寫出 binary little-endian PLY。</summary>
    public static int SaveAsBinaryPly(IReadOnlyList<(float X, float Y, float Z)> points, string filePath)
    {
        if (points.Count == 0)
            throw new InvalidOperationException("No points to write.");

        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write);
        var header = new StringBuilder();
        header.Append("ply\n");
        header.Append("format binary_little_endian 1.0\n");
        header.Append("comment ThreeDScan HeightImagePlyExporter — units mm (scanner frame)\n");
        header.Append($"element vertex {points.Count}\n");
        header.Append("property float x\n");
        header.Append("property float y\n");
        header.Append("property float z\n");
        header.Append("end_header\n");
        var headerBytes = Encoding.ASCII.GetBytes(header.ToString());
        fs.Write(headerBytes, 0, headerBytes.Length);

        using var bw = new BinaryWriter(fs, Encoding.ASCII, leaveOpen: true);
        foreach (var (x, y, z) in points)
        {
            bw.Write(x);
            bw.Write(y);
            bw.Write(z);
        }

        return points.Count;
    }
}
