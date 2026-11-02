namespace ThreeDScan.Domain.Models;

/// <summary>段拍攝中繼資料（對外 JSON capture；對齊 Hank FrameMetadata）。</summary>
public sealed class SegmentCaptureMetadata
{
    /// <summary>影像寬度（像素）。</summary>
    public int Width { get; init; }

    /// <summary>Profile 列數。</summary>
    public int ProfileCount { get; init; }

    /// <summary>X 間距（mm）。</summary>
    public double PitchXmm { get; init; }

    /// <summary>Y 間距（mm）。</summary>
    public double PitchYmm { get; init; }

    /// <summary>Z 間距（mm）。</summary>
    public double PitchZmm { get; init; }

    /// <summary>掃描器 X 起點（mm）。</summary>
    public double ScannerXStartMm { get; init; }

    /// <summary>掃描器 Y 起點（mm）。</summary>
    public double ScannerYStartMm { get; init; }

    /// <summary>座標原點說明。</summary>
    public string CoordinateOrigin { get; init; } = "sdk-origin";

    /// <summary>座標系名稱。</summary>
    public string CoordinateSystem { get; init; } = "scanner";

    /// <summary>單位。</summary>
    public string Unit { get; init; } = "mm";
}
