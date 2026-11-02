namespace ThreeDScan.Infrastructure.Export.Models;

/// <summary>孔洞分析 ROI。</summary>
/// <remarks>由 Hank Scan3D 的 FeatureRoi 搬入，邏輯不改。</remarks>
public sealed class FeatureRoi
{
    /// <summary>建立 ROI。</summary>
    /// <param name="minimum">最小角。</param>
    /// <param name="maximum">最大角。</param>
    public FeatureRoi(FeaturePoint minimum, FeaturePoint maximum)
    {
        Minimum = minimum;
        Maximum = maximum;
    }

    /// <summary>最小角。</summary>
    public FeaturePoint Minimum { get; init; }

    /// <summary>最大角。</summary>
    public FeaturePoint Maximum { get; init; }
}
