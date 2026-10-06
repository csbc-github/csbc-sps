namespace ThreeDScan.Domain.Models;

/// <summary>線掃描特徵點，單位 mm。X 為掃描橫向，Y 為滾輪方向，Z 為滾輪平面以上的高度。</summary>
public sealed class MeasuredPoint
{
    /// <summary>橫向位置 (mm)。較小者為左邊。</summary>
    public double X { get; init; }

    /// <summary>沿滾輪位置 (mm)，由速度乘時間戳得到。</summary>
    public double Y { get; init; }

    /// <summary>相對滾輪平面的高度 (mm)。</summary>
    public double Z { get; init; }
}
