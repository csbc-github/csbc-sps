using System.Globalization;
using System.Text;

namespace PlyProcessor.Core;

public static class PlyExporter
{
    public static byte[] Export(PlyPoint[] pts, PlyPoint[]? rawPts = null, int[]? mapping = null)
    {
        bool hasRaw = rawPts != null && mapping != null;
        var sb = new StringBuilder();
        sb.AppendLine("ply");
        sb.AppendLine("format ascii 1.0");
        sb.AppendLine($"element vertex {pts.Length}");
        sb.AppendLine("property float x");
        sb.AppendLine("property float y");
        sb.AppendLine("property float z");
        sb.AppendLine("property float intensity");
        if (hasRaw) { sb.AppendLine("property float raw_x"); sb.AppendLine("property float raw_y"); sb.AppendLine("property float raw_z"); }
        sb.AppendLine("end_header");
        for (int i = 0; i < pts.Length; i++)
        {
            var p = pts[i];
            sb.Append(p.X.ToString("F6", CultureInfo.InvariantCulture)).Append(' ')
              .Append(p.Y.ToString("F6", CultureInfo.InvariantCulture)).Append(' ')
              .Append(p.Z.ToString("F6", CultureInfo.InvariantCulture)).Append(' ')
              .Append(p.Intensity);
            if (hasRaw)
            {
                int ri = mapping![i];
                if (ri >= 0 && ri < rawPts!.Length)
                    sb.Append(' ').Append(rawPts[ri].X.ToString("F6", CultureInfo.InvariantCulture))
                      .Append(' ').Append(rawPts[ri].Y.ToString("F6", CultureInfo.InvariantCulture))
                      .Append(' ').Append(rawPts[ri].Z.ToString("F6", CultureInfo.InvariantCulture));
                else sb.Append(" 0 0 0");
            }
            sb.AppendLine();
        }
        return Encoding.UTF8.GetBytes(sb.ToString());
    }
}
