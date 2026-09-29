namespace ThreeDScan.Application.Calibration.Models;

/// <summary>儀器校正 complete 最終 result（callback／GET 查詢）。</summary>
public sealed class InstrumentCalibrationCompleteResult
{
    /// <summary>掃描器編號。</summary>
    public string ScannerId { get; init; } = "";

    /// <summary>補償值 offsetX/Y/Z (mm)。</summary>
    public CompensationDto Compensation { get; init; } = new();

    /// <summary>XY 品質指標：球距誤差 RMSE (mm)。</summary>
    public double RmseXY { get; init; }

    /// <summary>Z 品質指標：兩次估計值絕對差 (mm)。</summary>
    public double ZDeviation { get; init; }

    /// <summary>校正完成時間（ISO 8601）。</summary>
    public string CalibratedAt { get; init; } = "";
}
