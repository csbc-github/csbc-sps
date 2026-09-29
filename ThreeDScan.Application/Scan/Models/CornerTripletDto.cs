using ThreeDScan.Application.Common.Models;

namespace ThreeDScan.Application.Scan.Models;

/// <summary>角鋼一端的三個特徵點。</summary>
public sealed class CornerTripletDto
{
    /// <summary>左邊角點座標 (mm)。</summary>
    public CoordinateDto LeftCorner { get; init; } = new();

    /// <summary>右邊角點座標 (mm)。</summary>
    public CoordinateDto RightCorner { get; init; } = new();

    /// <summary>頂點座標 (mm)。</summary>
    public CoordinateDto Apex { get; init; } = new();
}
