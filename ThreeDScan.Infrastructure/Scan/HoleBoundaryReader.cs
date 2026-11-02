using System.Text.Json;
using ThreeDScan.Application.Common.Models;
using ThreeDScan.Application.Scan;
using ThreeDScan.Application.Scan.Models;
using ThreeDScan.Domain.Models;

namespace ThreeDScan.Infrastructure.Scan;

/// <summary>解析 ContentsRoot/_monitor/latest-hole-boundary.json。</summary>
public sealed class HoleBoundaryReader : IHoleBoundaryReader
{
    /// <inheritdoc />
    public HoleBoundarySnapshot? TryRead(string contentsRoot)
    {
        var path = ScanMonitorPaths.HoleBoundaryFile(contentsRoot);
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var doc = JsonDocument.Parse(stream);
            if (!doc.RootElement.TryGetProperty("featureBoundary", out var boundary)
                && !doc.RootElement.TryGetProperty("FeatureBoundary", out boundary))
            {
                return null;
            }

            var points = new List<CoordinateDto>();
            foreach (var item in boundary.EnumerateArray())
            {
                points.Add(new CoordinateDto
                {
                    X = ReadNumber(item, "x", "X"),
                    Y = ReadNumber(item, "y", "Y"),
                    Z = ReadNumber(item, "z", "Z"),
                });
            }

            return new HoleBoundarySnapshot
            {
                Points = points,
            };
        }
        catch (IOException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>讀 camelCase 或 PascalCase 數值；都沒有則 0。</summary>
    /// <param name="element">JSON 物件。</param>
    /// <param name="camel">小寫開頭欄位。</param>
    /// <param name="pascal">大寫開頭欄位。</param>
    /// <returns>座標值。</returns>
    private static double ReadNumber(JsonElement element, string camel, string pascal)
    {
        if (element.TryGetProperty(camel, out var a) && a.TryGetDouble(out var va))
        {
            return va;
        }

        if (element.TryGetProperty(pascal, out var b) && b.TryGetDouble(out var vb))
        {
            return vb;
        }

        return 0;
    }
}
