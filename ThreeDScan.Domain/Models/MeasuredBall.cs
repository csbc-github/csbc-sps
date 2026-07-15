namespace ThreeDScan.Domain.Models;

/// <summary>單顆標準球的量測球心與直徑（掃描器座標系）。</summary>
public sealed class MeasuredBall
{
    public int Index { get; init; }
    public double X { get; init; }
    public double Y { get; init; }
    public double Z { get; init; }
    public double Diameter { get; init; }
    public bool IsWithinTolerance { get; init; }
}
