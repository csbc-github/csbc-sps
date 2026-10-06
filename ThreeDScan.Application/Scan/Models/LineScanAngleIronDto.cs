using ThreeDScan.Application.Common.Models;

namespace ThreeDScan.Application.Scan.Models;

/// <summary>指南線掃描單支角鋼（左右長度，無單一 length／orientation）。</summary>
public sealed class LineScanAngleIronDto
{
    /// <summary>角鋼序號。</summary>
    public int Index { get; init; }

    /// <summary>頭端左邊角／右邊角／頂點。</summary>
    public CornerTripletDto HeadFeatures { get; init; } = new();

    /// <summary>尾端左邊角／右邊角／頂點。</summary>
    public CornerTripletDto TailFeatures { get; init; } = new();

    /// <summary>該件全部點的 X 跨距 (mm)。</summary>
    public double Width { get; init; }

    /// <summary>高度（由頂點與角點高度差算出）。</summary>
    public double Height { get; init; }

    /// <summary>左邊長度（頭端左角點至尾端左角點之三維距離）。</summary>
    public double LeftLength { get; init; }

    /// <summary>右邊長度（頭端右角點至尾端右角點之三維距離）。</summary>
    public double RightLength { get; init; }

    /// <summary>整支角鋼沿滾輪的長度 (mm)。</summary>
    public double Length { get; init; }

    /// <summary>左邊翼寬 (mm)。頭尾截面各自量完再平均。</summary>
    public double LeftWidth { get; init; }

    /// <summary>右邊翼寬 (mm)。頭尾截面各自量完再平均。</summary>
    public double RightWidth { get; init; }
}
