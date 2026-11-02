namespace ThreeDScan.Domain.Models;

/// <summary>三維座標（mm）。對應 Hank <c>Scan3D.Processing.Point3</c>。</summary>
public sealed class Point3
{
    /// <summary>X（mm）。</summary>
    public double X { get; init; }

    /// <summary>Y（mm）。</summary>
    public double Y { get; init; }

    /// <summary>Z（mm）。</summary>
    public double Z { get; init; }
}
