namespace ThreeDScan.Application.Calibration.Models;

/// <summary>儀器校正計算結果（補償、配對明細）。</summary>
public sealed class InstrumentCalibrationComputeResult
{
    /// <summary>掃描器補償值 offset = 實際位置 − 預期位置 (mm)。</summary>
    public CompensationDto Compensation { get; init; } = new();

    /// <summary>校正殘差 RMSE (mm)。</summary>
    public double Rmse { get; init; }

    /// <summary>反推的掃描器實際絕對位置。</summary>
    public CoordinateDto ActualScannerPosition { get; init; } = new();

    /// <summary>各顆球的配對與殘差明細。</summary>
    public IReadOnlyList<MatchedBallDto> MatchedBalls { get; init; } = Array.Empty<MatchedBallDto>();

    /// <summary>量測推算的掃描器到地板（滾輪面）Z 距離 (mm)。</summary>
    public double ScannerToFloorZ { get; init; }
}
