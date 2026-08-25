namespace ThreeDScan.Application.Calibration.Models;

/// <summary>滾輪校正結果：掃描器到滾輪台的 Z 距離。</summary>
public sealed class RollerCalibrationData
{
    /// <summary>光達感測器編號。</summary>
    public string ScannerId { get; init; } = "";

    /// <summary>掃描器到滾輪台之 Z 軸距離 (mm)。</summary>
    public double ZDistance { get; init; }

    /// <summary>校正完成時間（ISO 8601）。</summary>
    public string CalibratedAt { get; init; } = "";

    /// <summary>本次呼叫追溯 ID。</summary>
    public string TraceId { get; init; } = "";
}
