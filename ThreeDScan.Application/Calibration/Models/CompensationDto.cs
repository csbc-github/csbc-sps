namespace ThreeDScan.Application.Calibration.Models;

/// <summary>三軸補償偏移量 (mm)。</summary>
public sealed class CompensationDto
{
    /// <summary>X 軸補償 (mm)。</summary>
    public double OffsetX { get; init; }

    /// <summary>Y 軸補償 (mm)。</summary>
    public double OffsetY { get; init; }

    /// <summary>Z 軸補償 (mm)。</summary>
    public double OffsetZ { get; init; }
}
