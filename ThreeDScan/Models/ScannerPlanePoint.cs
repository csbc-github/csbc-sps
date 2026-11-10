namespace ThreeDScan.Models;

/// <summary>掃描器平面座標點 (mm)，供剖面點選。</summary>
public sealed class ScannerPlanePoint
{
    /// <summary>掃描器 X。</summary>
    public double X { get; init; }

    /// <summary>掃描器 Y。</summary>
    public double Y { get; init; }
}
