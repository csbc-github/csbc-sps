namespace ThreeDScan.Infrastructure.Export.Models;

/// <summary>點雲軸對齊包圍盒。對應 Hank <c>ScanBounds</c>。</summary>
public sealed class ScanBounds
{
    /// <summary>X 最小。</summary>
    public double MinX { get; init; }

    /// <summary>X 最大。</summary>
    public double MaxX { get; init; }

    /// <summary>Y 最小。</summary>
    public double MinY { get; init; }

    /// <summary>Y 最大。</summary>
    public double MaxY { get; init; }

    /// <summary>Z 最小。</summary>
    public double MinZ { get; init; }

    /// <summary>Z 最大。</summary>
    public double MaxZ { get; init; }

    /// <summary>由點集計算包圍盒。</summary>
    /// <param name="points">點。</param>
    public static ScanBounds Of(IEnumerable<ScanVector> points)
    {
        var minX = double.PositiveInfinity;
        var minY = double.PositiveInfinity;
        var minZ = double.PositiveInfinity;
        var maxX = double.NegativeInfinity;
        var maxY = double.NegativeInfinity;
        var maxZ = double.NegativeInfinity;
        foreach (var p in points)
        {
            minX = Math.Min(minX, p.X);
            maxX = Math.Max(maxX, p.X);
            minY = Math.Min(minY, p.Y);
            maxY = Math.Max(maxY, p.Y);
            minZ = Math.Min(minZ, p.Z);
            maxZ = Math.Max(maxZ, p.Z);
        }

        return new ScanBounds
        {
            MinX = minX,
            MaxX = maxX,
            MinY = minY,
            MaxY = maxY,
            MinZ = minZ,
            MaxZ = maxZ,
        };
    }
}
