namespace ThreeDScan.Infrastructure.Export.Models;

/// <summary>開口寬度範圍。</summary>
/// <remarks>由 Hank Scan3D 的 OpeningWidthRange 搬入，邏輯不改。</remarks>
public sealed class OpeningWidthRange
{
    /// <summary>建立範圍。</summary>
    /// <param name="min">最小。</param>
    /// <param name="max">最大。</param>
    /// <param name="median">中位。</param>
    public OpeningWidthRange(double min, double max, double median)
    {
        Min = min;
        Max = max;
        Median = median;
    }

    /// <summary>最小。</summary>
    public double Min { get; init; }

    /// <summary>最大。</summary>
    public double Max { get; init; }

    /// <summary>中位。</summary>
    public double Median { get; init; }
}
