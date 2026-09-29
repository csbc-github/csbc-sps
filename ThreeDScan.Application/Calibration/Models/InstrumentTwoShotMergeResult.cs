using ThreeDScan.Application.Common.Models;

namespace ThreeDScan.Application.Calibration.Models;

/// <summary>兩次拍攝合併結果。</summary>
public sealed class InstrumentTwoShotMergeResult
{
    /// <summary>掃描器補償值 (mm)。</summary>
    public CompensationDto Compensation { get; init; } = new();

    /// <summary>掃描器實際位置 (mm)。</summary>
    public CoordinateDto ActualScannerPosition { get; init; } = new();

    /// <summary>掃描器至地面 Z (mm)。</summary>
    public double ScannerToFloorZ { get; init; }

    /// <summary>兩次拍攝的 Z 偏差 (mm)。</summary>
    public double ZDeviation { get; init; }

    /// <summary>XY 平面 RMSE (mm)。</summary>
    public double RmseXY { get; init; }

    /// <summary>品質是否通過門檻。</summary>
    public bool QualityOk { get; init; }
}
