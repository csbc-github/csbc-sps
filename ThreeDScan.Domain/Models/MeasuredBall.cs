namespace ThreeDScan.Domain.Models;

/// <summary>單顆標準球的量測球心與直徑（掃描器座標系）。</summary>
public sealed class MeasuredBall
{
    /// <summary>球序號。</summary>
    public int Index { get; init; }

    /// <summary>球心 X (mm)。</summary>
    public double X { get; init; }

    /// <summary>球心 Y (mm)。</summary>
    public double Y { get; init; }

    /// <summary>球心 Z (mm)。</summary>
    public double Z { get; init; }

    /// <summary>自由半徑球面擬合的實測直徑 (mm)，不以標準球尺寸覆寫。</summary>
    public double Diameter { get; init; }

    /// <summary>自由估徑是否在球徑容差內；與球心可否用於校正分開判定。</summary>
    public bool IsWithinTolerance { get; init; }

    /// <summary>已知標準尺寸的球心模型是否通過獨立球面品質驗證。</summary>
    public bool KnownRadiusModelValidated { get; init; }

    /// <summary>是否可用於校正配對；舊有直徑合格球仍相容。</summary>
    public bool EligibleForCalibration => (IsWithinTolerance && FreeMeasurementQualityPassed != false)
        || KnownRadiusModelValidated;

    /// <summary>球心擬合方式：FreeRadiusRobust 或 KnownRadiusCenter。</summary>
    public string SphereFitMethod { get; init; } = "FreeRadiusRobust";

    /// <summary>球心是否使用已知標準半徑求解；Diameter 仍保留自由估徑。</summary>
    public bool UsesKnownRadius { get; init; }

    /// <summary>求球心所依據的標準直徑 (mm)；舊資料可能未提供。</summary>
    public double? NominalDiameterMm { get; init; }

    /// <summary>原全點自由球面品質是否通過；舊資料未提供時為 null。</summary>
    public bool? FreeMeasurementQualityPassed { get; init; }

    /// <summary>是否以局部異常點驗證及球心穩定性檢查確認校正資格。</summary>
    public bool UsedLocalizedOutlierValidation { get; init; }

    /// <summary>局部分區移除後最大球心變動 (mm)；僅局部驗證時提供。</summary>
    public double? CenterSensitivityMm { get; init; }
}
