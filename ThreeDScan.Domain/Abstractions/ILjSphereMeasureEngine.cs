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

    /// <summary>
    /// 在已擷取影像上建立／更新 Ref：以 ScannerConfig 種子尋球、重訓樣板、寫出 ref_height/gray.png。
    /// 回傳的 Ref* 是尋球擬合球心，呼叫端會寫回 DB（可能與種子不同）。
    /// </summary>
    Task<ReferenceSetupResult> SetupReferenceAsync(
        string scannerId,
        ScannerConfigRecord? existingConfig,
        ScannerCaptureResult capture,
        LjCalibrationParameters parameters,
        string contentsFolder,
        CancellationToken ct = default);
}
