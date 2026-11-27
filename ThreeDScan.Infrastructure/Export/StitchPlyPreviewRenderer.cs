using System.Drawing;
using System.Drawing.Imaging;
using ThreeDScan.Infrastructure.Export.Models;

namespace ThreeDScan.Infrastructure.Export;

/// <summary>拼接 PLY 三視角預覽 PNG（Hank ArtifactService.Render）。</summary>
public static class StitchPlyPreviewRenderer
{
    /// <summary>將點雲投影為 top／front／side PNG。</summary>
    /// <param name="cloud">拼接點雲。</param>
    /// <param name="path">輸出 PNG 路徑。</param>
    /// <param name="view">視角名稱：top、front、side。</param>
    public static void Render(ScanCloud cloud, string path, string view)
        => Render(cloud.Points.Select(point => point.Position), path, view);

    /// <summary>從本次拼接已計算的原始精度座標建立相同預覽，免再解析正式 PLY。</summary>
    public static void Render(IEnumerable<ScanVector> positions, string path, string view)
    {
        const int width = 1200;
        const int height = 760;
        const int margin = 35;
        (double X, double Y) Project(ScanVector p)
        {
            if (view == "top")
            {
                return (p.X, p.Y);
            }

            if (view == "front")
            {
                return (p.X, p.Z);
            }

            return (p.Y, p.Z);
        }

        var points = positions.Select(Project).ToArray();
        double minX = points.Min(p => p.X);
        double maxX = points.Max(p => p.X);
        double minY = points.Min(p => p.Y);
        double maxY = points.Max(p => p.Y);
        double scale = Math.Min(
            (width - margin * 2) / Math.Max(0.001, maxX - minX),
            (height - margin * 2) / Math.Max(0.001, maxY - minY));
        using var bitmap = new Bitmap(width, height);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.White);
        using var brush = new SolidBrush(Color.FromArgb(35, 105, 140));
        int stride = Math.Max(1, points.Length / 250000);
        for (int i = 0; i < points.Length; i += stride)
        {
            var p = points[i];
            graphics.FillRectangle(
                brush,
                (float)(margin + (p.X - minX) * scale),
                (float)(height - margin - (p.Y - minY) * scale),
                1.4f,
                1.4f);
        }

        bitmap.Save(path, ImageFormat.Png);
    }
}
