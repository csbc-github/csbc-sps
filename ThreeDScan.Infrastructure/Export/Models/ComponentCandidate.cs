namespace ThreeDScan.Infrastructure.Export.Models;

/// <summary>單段候選物件。對應 Hank <c>Scan3D.Processing.ComponentCandidate</c>。</summary>
public sealed class ComponentCandidate
{
    /// <summary>抽取 rank。</summary>
    public int Rank { get; init; }

    /// <summary>點數。</summary>
    public int PointCount { get; init; }

    /// <summary>連通格數。</summary>
    public int CellCount { get; init; }

    /// <summary>掃描器座標包圍盒。</summary>
    public ScanBounds ScannerBounds { get; init; } = new ScanBounds();

    /// <summary>是否兩翼。</summary>
    public bool IsTwoWing { get; init; }

    /// <summary>信心分數 0～1。</summary>
    public double Confidence { get; init; }

    /// <summary>左翼 RMS（mm）。</summary>
    public double? LeftPlaneRmsMm { get; init; }

    /// <summary>右翼 RMS（mm）。</summary>
    public double? RightPlaneRmsMm { get; init; }

    /// <summary>兩翼法線夾角（度）。</summary>
    public double? WingNormalAngleDegrees { get; init; }

    /// <summary>是否平面物件。</summary>
    public bool IsPlanar { get; init; }

    /// <summary>無法擬合原因。</summary>
    public string? Reason { get; init; }
}
