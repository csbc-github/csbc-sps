namespace ThreeDScan.Application.Calibration.Models;

/// <summary>兩次拍攝合併結果。</summary>
public sealed class InstrumentTwoShotMergeResult
{
    public CompensationDto Compensation { get; init; } = new();
    public CoordinateDto ActualScannerPosition { get; init; } = new();
    public double ScannerToFloorZ { get; init; }
    public double ZDeviation { get; init; }
    public double RmseXY { get; init; }
    public bool QualityOk { get; init; }
}
