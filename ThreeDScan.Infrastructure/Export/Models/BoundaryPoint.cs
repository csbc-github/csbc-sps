namespace ThreeDScan.Infrastructure.Export.Models;

/// <summary>孔洞邊界點。</summary>
/// <remarks>由 Hank Scan3D 的 BoundaryPoint 搬入，邏輯不改。</remarks>
public sealed class BoundaryPoint
{
    /// <summary>建立邊界點。</summary>
    /// <param name="sourcePointIndex">來源點索引。</param>
    /// <param name="xMm">X（mm）。</param>
    /// <param name="yMm">Y（mm）。</param>
    /// <param name="zMm">Z（mm）。</param>
    /// <param name="uMm">面內 U（mm）。</param>
    /// <param name="vMm">面內 V（mm）。</param>
    /// <param name="planeDistanceMm">到平面距離（mm）。</param>
    public BoundaryPoint(
        int sourcePointIndex,
        double xMm,
        double yMm,
        double zMm,
        double uMm,
        double vMm,
        double planeDistanceMm)
    {
        SourcePointIndex = sourcePointIndex;
        XMm = xMm;
        YMm = yMm;
        ZMm = zMm;
        UMm = uMm;
        VMm = vMm;
        PlaneDistanceMm = planeDistanceMm;
    }

    /// <summary>來源點索引。</summary>
    public int SourcePointIndex { get; init; }

    /// <summary>X（mm）。</summary>
    public double XMm { get; init; }

    /// <summary>Y（mm）。</summary>
    public double YMm { get; init; }

    /// <summary>Z（mm）。</summary>
    public double ZMm { get; init; }

    /// <summary>面內 U（mm）。</summary>
    public double UMm { get; init; }

    /// <summary>面內 V（mm）。</summary>
    public double VMm { get; init; }

    /// <summary>到平面距離（mm）。</summary>
    public double PlaneDistanceMm { get; init; }
}
