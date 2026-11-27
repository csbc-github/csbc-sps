namespace ThreeDScan.Application.Calibration.Models;

/// <summary>單顆量測球結果（掃描器座標系）。</summary>
public sealed class MeasuredBallDto
{
    /// <summary>球索引（0 起）。</summary>
    public int Index { get; init; }

    /// <summary>球心 X (mm)。</summary>
    public double X { get; init; }

    /// <summary>球心 Y (mm)。</summary>
    public double Y { get; init; }

    /// <summary>球心 Z (mm)。</summary>
    public double Z { get; init; }

    /// <summary>自由半徑擬合的實測直徑 (mm)，不以標準尺寸取代。</summary>
    public double Diameter { get; init; }

    /// <summary>直徑是否在允許誤差內。</summary>
    public bool Ok { get; init; }

    /// <summary>已知標準半徑的球心模型是否通過獨立品質驗證。</summary>
    public bool KnownRadiusModelValidated { get; init; }

    /// <summary>球心可否用於校正；不代表自由估徑符合容差。</summary>
    public bool EligibleForCalibration => (Ok && FreeMeasurementQualityPassed != false)
        || KnownRadiusModelValidated;

    /// <summary>球心擬合方式：FreeRadiusRobust 或 KnownRadiusCenter。</summary>
    public string SphereFitMethod { get; init; } = "FreeRadiusRobust";

    /// <summary>是否以標準半徑求球心；Diameter 與 Ok 仍是自由估徑診斷。</summary>
    public bool UsesKnownRadius { get; init; }

    /// <summary>球心模型所用標準直徑 (mm)。</summary>
    public double? NominalDiameterMm { get; init; }

    /// <summary>原全點自由球面品質；null 表示舊資料未提供此診斷。</summary>
    public bool? FreeMeasurementQualityPassed { get; init; }

    /// <summary>校正球心是否通過局部異常點與球心穩定性驗證。</summary>
    public bool UsedLocalizedOutlierValidation { get; init; }

    /// <summary>分區移除後最大球心變動 (mm)。</summary>
    public double? CenterSensitivityMm { get; init; }
}
