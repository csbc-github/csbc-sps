using System.Globalization;
using System.Text;

namespace PlyProcessor.Core;

public static class JsonExporter
{
    /// <summary>
    /// Exports per-object coordinate data.
    /// Outer structure is pretty-printed (indent 2); each point row is compact on one line.
    /// Matches the format of tools/json_formatter.py (ensure_ascii=false, indent=2).
    /// </summary>
    public static byte[] Export(ClusterObject obj, PlyPoint[]? rawPts = null, int[]? mapping = null)
    {
        bool hasRaw = rawPts != null && mapping != null;
        var sb = new StringBuilder();
        sb.AppendLine("{");
        sb.AppendLine($"  \"name\": \"{EscapeJson(obj.Name)}\",");
        sb.AppendLine($"  \"count\": {obj.Points.Length},");
        sb.AppendLine("  \"columns\": [\"x\",\"y\",\"z\",\"intensity\",\"raw_x\",\"raw_y\",\"raw_z\",\"raw_intensity\"],");
        sb.AppendLine("  \"note\": \"x/y/z/intensity 為對齊處理後的座標；raw_* 為掃描器原始座標（轉換前），無法比對時為 null\",");
        sb.AppendLine("  \"points\": [");
        for (int i = 0; i < obj.Points.Length; i++)
        {
            var p = obj.Points[i];
            sb.Append("    [");
            sb.Append(Round6(p.X)).Append(',').Append(Round6(p.Y)).Append(',').Append(Round6(p.Z)).Append(',').Append(p.Intensity);
            if (hasRaw)
            {
                int ri = mapping![i];
                if (ri >= 0 && ri < rawPts!.Length)
                {
                    var r = rawPts[ri];
                    sb.Append(',').Append(Round6(r.X)).Append(',').Append(Round6(r.Y)).Append(',').Append(Round6(r.Z)).Append(',').Append(r.Intensity);
                }
                else sb.Append(",null,null,null,null");
            }
            else sb.Append(",null,null,null,null");
            sb.Append(']');
            if (i < obj.Points.Length - 1) sb.Append(',');
            sb.AppendLine();
        }
        sb.AppendLine("  ]");
        sb.AppendLine("}");
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    static string Round6(float v) =>
        Math.Round(v, 6).ToString("G", CultureInfo.InvariantCulture);

    static string EscapeJson(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
