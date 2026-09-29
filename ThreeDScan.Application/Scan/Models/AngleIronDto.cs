using ThreeDScan.Application.Common.Models;

namespace ThreeDScan.Application.Scan.Models;

/// <summary>單支角鋼頭尾特徵與尺寸。</summary>
public sealed class AngleIronDto
{
    /// <summary>角鋼序號。</summary>
    public int Index { get; init; }

    /// <summary>頭端左邊角／右邊角／頂點（面掃描為本段前緣）。</summary>
    public CornerTripletDto HeadFeatures { get; init; } = new();

    /// <summary>尾端左邊角／右邊角／頂點。</summary>
    public CornerTripletDto TailFeatures { get; init; } = new();

    /// <summary>寬度（由左右角點算出）。</summary>
    public double Width { get; init; }

    /// <summary>高度（由頂點與角點高度差算出）。</summary>
    public double Height { get; init; }

    /// <summary>長度（由頭尾頂點連線算出）。</summary>
    public double Length { get; init; }

    /// <summary>擺放方向判定結果（依寬高比例；等邊角鋼依預設中心邏輯）。</summary>
    public string Orientation { get; init; } = "";
}
