namespace ThreeDScan.Infrastructure.Export.Models;

/// <summary>孔洞偵測參數。距離單位為 mm，預設對齊 hole-detector.js。</summary>
/// <remarks>由 Hank Scan3D 的 HoleDetectionOptions 搬入，邏輯不改。</remarks>
public sealed class HoleDetectionOptions
{
    /// <summary>使用預設參數。</summary>
    public HoleDetectionOptions()
    {
    }

    /// <summary>指定最大面數。</summary>
    /// <param name="maxFaces">最多平面數。</param>
    public HoleDetectionOptions(int maxFaces)
    {
        MaxFaces = maxFaces;
    }

    /// <summary>平面擬合容差（mm）。</summary>
    public double FitToleranceMm { get; init; } = .8;

    /// <summary>表面歸屬容差（mm）。</summary>
    public double SurfaceToleranceMm { get; init; } = 1.5;

    /// <summary>遮擋容差（mm）。</summary>
    public double OcclusionToleranceMm { get; init; } = 3;

    /// <summary>最小開口（mm）。</summary>
    public double MinimumOpeningMm { get; init; } = 3;

    /// <summary>最少掃描列數。</summary>
    public int MinimumScanlines { get; init; } = 4;

    /// <summary>最多平面數。</summary>
    public int MaxFaces { get; init; } = 4;
}
