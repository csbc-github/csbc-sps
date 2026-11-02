namespace ThreeDScan.Infrastructure.Export.Models;

/// <summary>孔洞特徵點（mm）。</summary>
/// <remarks>由 Hank Scan3D 的 FeaturePoint 搬入，邏輯不改。</remarks>
public sealed class FeaturePoint
{
    /// <summary>建立特徵點。</summary>
    /// <param name="x">X（mm）。</param>
    /// <param name="y">Y（mm）。</param>
    /// <param name="z">Z（mm）。</param>
    public FeaturePoint(double x, double y, double z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    /// <summary>X（mm）。</summary>
    public double X { get; init; }

    /// <summary>Y（mm）。</summary>
    public double Y { get; init; }

    /// <summary>Z（mm）。</summary>
    public double Z { get; init; }
}
