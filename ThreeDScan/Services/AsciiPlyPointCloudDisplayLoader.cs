using System.Globalization;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using ThreeDScan.Models;

namespace ThreeDScan.Services;

/// <summary>從 ASCII PLY 載入 WPF 顯示用點雲（噴印／研磨區共用）。</summary>
public static class AsciiPlyPointCloudDisplayLoader
{
    /// <summary>WPF 端效能上限；超過時等距跳點。</summary>
    public const int MaxDisplayPoints = 60_000;

    /// <summary>顯示用點雲：不 flatten／不 voxel；超過上限時等距跳點。</summary>
    /// <param name="plyPath">PLY 絕對路徑。</param>
    /// <param name="token">取消權杖。</param>
    /// <returns>著色後的頂點清單；無法讀取時為空陣列。</returns>
    public static async Task<IReadOnlyList<PointCloudRenderVertex>> LoadAsync(string plyPath, CancellationToken token)
    {
        return await Task.Run(() =>
        {
            if (!File.Exists(plyPath)
                || !TryParseAsciiPlyPoints(plyPath, token, out var raw)
                || raw.Count == 0)
            {
                return (IReadOnlyList<PointCloudRenderVertex>)Array.Empty<PointCloudRenderVertex>();
            }

            int n = raw.Count;
            int step = 1;
            if (MaxDisplayPoints > 0 && n > MaxDisplayPoints)
            {
                step = (n + MaxDisplayPoints - 1) / MaxDisplayPoints;
            }

            var picked = new List<(double X, double Y, double Z)>(
                Math.Min(n, MaxDisplayPoints > 0 ? MaxDisplayPoints : n));
            for (int i = 0; i < n; i += step)
            {
                token.ThrowIfCancellationRequested();
                picked.Add(raw[i]);
                if (MaxDisplayPoints > 0 && picked.Count >= MaxDisplayPoints)
                {
                    break;
                }
            }

            ComputeBounds(picked, out var minX, out var maxX, out var minY, out var maxY, out var minZ, out var maxZ);

            var result = new List<PointCloudRenderVertex>(picked.Count);
            foreach (var p in picked)
            {
                var c = RainbowColorForVertex(p.X, p.Y, p.Z, minX, maxX, minY, maxY, minZ, maxZ);
                result.Add(new PointCloudRenderVertex(new Point3D(p.X, p.Y, p.Z), c));
            }

            return (IReadOnlyList<PointCloudRenderVertex>)result;
        }, token);
    }

    /// <summary>僅解析 ASCII 格式 PLY 的頂點座標列。</summary>
    /// <param name="plyPath">PLY 路徑。</param>
    /// <param name="token">取消權杖。</param>
    /// <param name="points">成功時的 (X,Y,Z) 清單。</param>
    /// <returns>是否讀到至少一個頂點；二進位 PLY 回 false。</returns>
    private static bool TryParseAsciiPlyPoints(string plyPath, CancellationToken token, out List<(double X, double Y, double Z)> points)
    {
        points = new List<(double, double, double)>();
        bool isAscii = false;
        bool inHeader = true;
        int vertexCount = 0;
        int dataIndex = 0;

        foreach (var rawLine in File.ReadLines(plyPath))
        {
            token.ThrowIfCancellationRequested();
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (inHeader)
            {
                if (line.StartsWith("format ", StringComparison.OrdinalIgnoreCase))
                {
                    isAscii = line.Contains("ascii", StringComparison.OrdinalIgnoreCase);
                }
                else if (line.StartsWith("element vertex ", StringComparison.OrdinalIgnoreCase))
                {
                    var countText = line["element vertex ".Length..].Trim();
                    if (int.TryParse(countText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) && count > 0)
                    {
                        vertexCount = count;
                    }
                }
                else if (line.Equals("end_header", StringComparison.OrdinalIgnoreCase))
                {
                    inHeader = false;
                    if (!isAscii)
                    {
                        points = new List<(double, double, double)>();
                        return false;
                    }
                }

                continue;
            }

            if (vertexCount > 0 && dataIndex >= vertexCount)
            {
                break;
            }

            dataIndex++;

            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3)
            {
                continue;
            }

            if (double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
                && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y)
                && double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var z))
            {
                points.Add((x, y, z));
            }
        }

        return points.Count > 0;
    }

    /// <summary>計算點集包絡盒，供高度著色正規化。</summary>
    private static void ComputeBounds(
        IReadOnlyList<(double X, double Y, double Z)> pts,
        out double minX, out double maxX, out double minY, out double maxY, out double minZ, out double maxZ)
    {
        minX = minY = minZ = double.MaxValue;
        maxX = maxY = maxZ = double.MinValue;
        foreach (var p in pts)
        {
            if (p.X < minX)
            {
                minX = p.X;
            }

            if (p.X > maxX)
            {
                maxX = p.X;
            }

            if (p.Y < minY)
            {
                minY = p.Y;
            }

            if (p.Y > maxY)
            {
                maxY = p.Y;
            }

            if (p.Z < minZ)
            {
                minZ = p.Z;
            }

            if (p.Z > maxZ)
            {
                maxZ = p.Z;
            }
        }
    }

    /// <summary>依最長軸跨度對單點做彩虹高度色。</summary>
    private static Color RainbowColorForVertex(
        double x, double y, double z,
        double minX, double maxX, double minY, double maxY, double minZ, double maxZ)
    {
        var sx = maxX - minX;
        var sy = maxY - minY;
        var sz = maxZ - minZ;
        if (sz >= sx && sz >= sy)
        {
            return HeightToRgbColor(z, minZ, maxZ);
        }

        if (sy >= sx)
        {
            return HeightToRgbColor(y, minY, maxY);
        }

        return HeightToRgbColor(x, minX, maxX);
    }

    /// <summary>將標量值映射到離散彩虹 RGB。</summary>
    private static Color HeightToRgbColor(double v, double lo, double hi)
    {
        var span = Math.Max(hi - lo, 1e-12);
        var t = (v - lo) / span;
        if (t < 0)
        {
            t = 0;
        }
        else if (t > 1)
        {
            t = 1;
        }

        const int levels = 220;
        t = Math.Round(t * (levels - 1)) / (levels - 1);
        var h = 0.75 * (1.0 - t);
        HsvToRgb(h, 0.88, 0.96, out var r, out var g, out var b);
        return Color.FromRgb(
            (byte)Math.Clamp((int)(r * 255.0), 0, 255),
            (byte)Math.Clamp((int)(g * 255.0), 0, 255),
            (byte)Math.Clamp((int)(b * 255.0), 0, 255));
    }

    /// <summary>HSV（0–1）轉線性 RGB。</summary>
    private static void HsvToRgb(double h, double s, double v, out double r, out double g, out double b)
    {
        if (s <= 0)
        {
            r = g = b = v;
            return;
        }

        var hh = h * 6.0;
        var i = (int)Math.Floor(hh);
        var f = hh - i;
        var p = v * (1.0 - s);
        var q = v * (1.0 - s * f);
        var t = v * (1.0 - s * (1.0 - f));
        switch (i % 6)
        {
            case 0: r = v; g = t; b = p; break;
            case 1: r = q; g = v; b = p; break;
            case 2: r = p; g = v; b = t; break;
            case 3: r = p; g = q; b = v; break;
            case 4: r = t; g = p; b = v; break;
            default: r = v; g = p; b = q; break;
        }
    }
}