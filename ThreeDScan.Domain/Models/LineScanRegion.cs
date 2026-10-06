namespace ThreeDScan.Domain.Models;

/// <summary>
/// 手動框選的掃描器平面範圍。量測只用 X 範圍挑點，高度一律由滾輪校正基準決定。
/// </summary>
public sealed class LineScanRegion
{
    /// <summary>掃描器 X 下限 (mm)。</summary>
    public double XMin { get; set; }

    /// <summary>掃描器 X 上限 (mm)。</summary>
    public double XMax { get; set; }

    /// <summary>掃描器 Y 下限 (mm)。只用來在剖面圖上重畫這個框，不限制量測。</summary>
    public double YMin { get; set; }

    /// <summary>掃描器 Y 上限 (mm)。只用來在剖面圖上重畫這個框，不限制量測。</summary>
    public double YMax { get; set; }

    /// <summary>
    /// 這個掃描器 X 是否落在框內（含邊界）。
    /// 線掃描器的 Y 是測距方向，拿它篩點等於設高度上限，會把高過框上緣的角鋼頂端切掉，所以量測只看 X。
    /// </summary>
    public bool ContainsAlongX(double x)
    {
        return x >= XMin && x <= XMax;
    }
}
