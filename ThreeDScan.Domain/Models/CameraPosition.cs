namespace ThreeDScan.Domain.Models;

/// <summary>掃描當下 DAS 期望掃描器到位的機台座標 (mm)。</summary>
public sealed class CameraPosition
{
    /// <summary>X 軸座標 (mm)。</summary>
    public double X { get; init; }

    /// <summary>Y 軸座標 (mm)。</summary>
    public double Y { get; init; }

    /// <summary>Z 軸座標 (mm)。</summary>
    public double Z { get; init; }
}
