using ThreeDScan.Application.Calibration.Dtos;

namespace ThreeDScan.Application.Scan.Dtos;

/// <summary>線掃描結果：偵測到的角鋼特徵與尺寸。</summary>
public sealed class LineScanData
{
    /// <summary>正常結束。</summary>
    public const string StatusCompleted = "Completed";

    /// <summary>超時結束，部分結果。</summary>
    public const string StatusPartialTimeout = "PartialTimeout";

    /// <summary>本次偵測到的角鋼總數。</summary>
    public int DetectedCount { get; init; }

    /// <summary><see cref="StatusCompleted"/> 或 <see cref="StatusPartialTimeout"/>。</summary>
    public string ScanStatus { get; init; } = "";

    /// <summary>本次呼叫追溯 ID。</summary>
    public string TraceId { get; init; } = "";

    /// <summary>各支角鋼特徵。</summary>
    public IReadOnlyList<AngleIronDto> AngleIrons { get; init; } = [];
}

/// <summary>單支角鋼頭尾特徵與尺寸。</summary>
public sealed class AngleIronDto
{
    /// <summary>角鋼序號。</summary>
    public int Index { get; init; }

    /// <summary>頭端左邊角／右邊角／頂點。</summary>
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
