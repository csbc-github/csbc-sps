namespace ThreeDScan.Application.Scan.Models;

/// <summary>面掃描段品質摘要（Hank segment response quality）。</summary>
public sealed class SurfaceScanSegmentQuality
{
    /// <summary>左翼平面 RMS（mm）。</summary>
    public double LeftPlaneRmsMm { get; init; }

    /// <summary>右翼平面 RMS（mm）。</summary>
    public double RightPlaneRmsMm { get; init; }

    /// <summary>翼板法向夾角（度）。</summary>
    public double WingNormalAngleDegrees { get; init; }

    /// <summary>選用的候選 rank。</summary>
    public int? SelectedComponentRank { get; init; }
}
