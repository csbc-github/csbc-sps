namespace ThreeDScan.Domain.Models;

/// <summary>單支角鋼的頭尾角點與尺寸。尺寸取中段截面的中位數，角點是兩翼落回滾輪面的位置。</summary>
public sealed class MeasuredAngleIron
{
    /// <summary>由左至右的序號，從 1 起算。</summary>
    public int Index { get; init; }

    /// <summary>頭端左角點。</summary>
    public MeasuredPoint HeadLeft { get; init; } = new();

    /// <summary>頭端右角點。</summary>
    public MeasuredPoint HeadRight { get; init; } = new();

    /// <summary>頭端頂點。</summary>
    public MeasuredPoint HeadApex { get; init; } = new();

    /// <summary>尾端左角點。</summary>
    public MeasuredPoint TailLeft { get; init; } = new();

    /// <summary>尾端右角點。</summary>
    public MeasuredPoint TailRight { get; init; } = new();

    /// <summary>尾端頂點。</summary>
    public MeasuredPoint TailApex { get; init; } = new();

    /// <summary>底寬，兩翼尖的 X 跨距，取中段截面的中位數 (mm)。</summary>
    public double Width { get; init; }

    /// <summary>高度，最高點扣掉當地滾輪面，取中段截面的中位數 (mm)。</summary>
    public double Height { get; init; }

    /// <summary>整支角鋼沿滾輪的長度 (mm)。</summary>
    public double Length { get; init; }

    /// <summary>左邊翼寬，頂點到左翼尖的距離，取中段截面的中位數 (mm)。</summary>
    public double LeftWidth { get; init; }

    /// <summary>右邊翼寬，頂點到右翼尖的距離，取中段截面的中位數 (mm)。</summary>
    public double RightWidth { get; init; }

    /// <summary>頭端左角點到尾端左角點的三維距離 (mm)。</summary>
    public double LeftLength { get; init; }

    /// <summary>頭端右角點到尾端右角點的三維距離 (mm)。</summary>
    public double RightLength { get; init; }
}
