namespace ThreeDScan.Domain.Models;

/// <summary>掃描器座標 ROI。對應 Hank <c>Scan3D.Processing.ScannerRoi</c>。</summary>
public sealed class ScannerRoi
{
    /// <summary>X 最小（mm）。</summary>
    public double MinX { get; init; }

    /// <summary>X 最大（mm）。</summary>
    public double MaxX { get; init; }

    /// <summary>Y 最小（mm）。</summary>
    public double MinY { get; init; }

    /// <summary>Y 最大（mm）。</summary>
    public double MaxY { get; init; }

    /// <summary>Z 最小（mm）。</summary>
    public double MinZ { get; init; }

    /// <summary>Z 最大（mm）。</summary>
    public double MaxZ { get; init; }
}
