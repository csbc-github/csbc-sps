using ThreeDScan.Domain.Models;

namespace ThreeDScan.Domain.Abstractions;

/// <summary>LJ Developer PM + Sphere (ljd_measure)。</summary>
public interface ILjSphereMeasureEngine
{
    /// <summary>對已擷取影像執行 Pattern Match 尋球與球心量測（儀器校正 UC-01）。</summary>
    Task<SphereMeasureResult> MeasureAsync(
        string scannerId,
        ScannerConfigRecord config,
        ScannerCaptureResult capture,
        LjCalibrationParameters parameters,
        CancellationToken ct = default);

    /// <summary>建立或更新 Ref 參考影像與種子座標（開發用 Ref 建立 API）。</summary>
    Task<ReferenceSetupResult> SetupReferenceAsync(
        string scannerId,
        ScannerConfigRecord? existingConfig,
        ScannerCaptureResult capture,
        LjCalibrationParameters parameters,
        string contentsFolder,
        CancellationToken ct = default);
}

/// <summary>LJ 量測參數（appsettings 預設值，可與 ScannerConfig 合併）。</summary>
public sealed class LjCalibrationParameters
{
    public double ExpectedBallDiameter { get; init; } = 24.9;
    public double DiameterTolerance { get; init; } = 1.0;
    public int BallCount { get; init; } = 3;
    public double MaxDiameterError { get; init; } = 5.0;

    /// <summary>滾輪／基準面平面 ROI（LPlaneTool）。</summary>
    public DatumPlaneSettings DatumPlane { get; init; } = new();
}

/// <summary>基準面（滾輪）平面量測 ROI，對應 ScannerConfig DatumPlane* 欄位。</summary>
public sealed class DatumPlaneSettings
{
    public bool Enabled { get; init; } = true;

    public double CenterX { get; init; } = 160;

    public double CenterY { get; init; } = 120;

    public double Width { get; init; } = 120;

    public double Height { get; init; } = 80;

    public double Angle { get; init; }
}
