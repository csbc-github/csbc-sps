using ThreeDScan.Application.Calibration.Models;

namespace ThreeDScan.Application.Calibration;

/// <summary>校正應用服務：UC-01 儀器校正與 Ref 建立。</summary>
public interface ICalibrationAppService
{
    /// <summary>
    /// UC-01 儀器校正：掃描標準球 → 比對台船已知座標 → 計算補償 offset → 寫入 CompensationHistory 並推送 WPF 預覽。
    /// 任一步驟失敗皆推送預覽並回傳對應錯誤碼，不寫入補償。
    /// </summary>
    Task<ApiResponse<InstrumentCalibrationData>> CalibrateInstrumentAsync(
        InstrumentCalibrationRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// 開發用 Ref 建立：拍攝一張 → 在 DB 指定的 Ref XY 擬合 → 覆寫 ScannerConfig.Ref* 與 ref_*.png。
    /// 不推送 WPF。不搜尋附近；寫回的是該點擬合球心。
    /// </summary>
    Task<ApiResponse<ReferenceSetupData>> SetupReferenceAsync(
        ReferenceSetupRequest request,
        CancellationToken ct = default);

    /// <summary>無校正物時掃描滾輪台，取得掃描器到滾輪的 Z 距離。</summary>
    Task<ApiResponse<RollerCalibrationData>> CalibrateRollerAsync(
        RollerCalibrationRequest request,
        CancellationToken ct = default);
}
