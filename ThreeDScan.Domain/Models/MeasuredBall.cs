namespace ThreeDScan.Domain.Models;

/// <summary>單顆標準球的量測球心與直徑（掃描器座標系）。</summary>
public sealed class MeasuredBall
{
    /// <summary>球序號。</summary>
    public int Index { get; init; }

    /// <summary>球心 X (mm)。</summary>
    public double X { get; init; }

    /// <summary>球心 Y (mm)。</summary>
    public double Y { get; init; }

    /// <summary>球心 Z (mm)。</summary>
    public double Z { get; init; }

    /// <summary>量測直徑 (mm)。</summary>
    public double Diameter { get; init; }

    /// <summary>是否在容差內。</summary>
    public bool IsWithinTolerance { get; init; }
}
