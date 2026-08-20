namespace ThreeDScan.Application.Calibration.Models;

/// <summary>單顆量測球結果（掃描器座標系）。</summary>
public sealed class MeasuredBallDto
{
    /// <summary>球索引（0 起）。</summary>
    public int Index { get; init; }

    /// <summary>球心 X (mm)。</summary>
    public double X { get; init; }

    /// <summary>球心 Y (mm)。</summary>
    public double Y { get; init; }

    /// <summary>球心 Z (mm)。</summary>
    public double Z { get; init; }

    /// <summary>擬合直徑 (mm)。</summary>
    public double Diameter { get; init; }

    /// <summary>直徑是否在允許誤差內。</summary>
    public bool Ok { get; init; }
}
