namespace ThreeDScan.Infrastructure.Export.Models;

/// <summary>同一掃描列的左右邊緣點。</summary>
/// <remarks>由 Hank Scan3D 的 HoleEdgePair 搬入，邏輯不改。</remarks>
public sealed class HoleEdgePair
{
    /// <summary>建立邊緣對。</summary>
    /// <param name="point1">左點。</param>
    /// <param name="point2">右點。</param>
    /// <param name="scanYmm">掃描列 Y（mm）。</param>
    public HoleEdgePair(BoundaryPoint point1, BoundaryPoint point2, double scanYmm)
    {
        Point1 = point1;
        Point2 = point2;
        ScanYmm = scanYmm;
    }

    /// <summary>左點。</summary>
    public BoundaryPoint Point1 { get; init; }

    /// <summary>右點。</summary>
    public BoundaryPoint Point2 { get; init; }

    /// <summary>掃描列 Y（mm）。</summary>
    public double ScanYmm { get; init; }
}
