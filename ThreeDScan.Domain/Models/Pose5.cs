namespace ThreeDScan.Domain.Models;

/// <summary>相機位姿。對應 Hank <c>Scan3D.Processing.Pose5</c>。</summary>
public sealed class Pose5
{
    /// <summary>X（mm）。</summary>
    public double X { get; init; }

    /// <summary>Y（mm）。</summary>
    public double Y { get; init; }

    /// <summary>Z（mm）。</summary>
    public double Z { get; init; }

    /// <summary>A 軸角度（度）。</summary>
    public double A { get; init; }

    /// <summary>C 軸角度（度）。</summary>
    public double C { get; init; }
}
