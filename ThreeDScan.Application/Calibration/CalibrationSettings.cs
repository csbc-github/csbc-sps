namespace ThreeDScan.Application.Calibration;

/// <summary>校正 API 行為設定（appsettings → Calibration）。</summary>
public sealed class CalibrationSettings
{
    /// <summary>
    /// 回應模式：<c>Production</c>（正式，僅規格必要欄位）或 <c>Debug</c>（含診斷欄位）。
    /// </summary>
    public string ResponseMode { get; set; } = "Production";

    /// <summary>寫入 OperationLog 的呼叫端識別（預設台船）。</summary>
    public string ClientId { get; set; } = "台船";

    /// <summary>
    /// Z 品質門檻 (mm)：兩次反推掃描器 Z 之差（<c>zDeviation</c>）須小於本值才寫入補償。預設 1.0。
    /// </summary>
    public double ZDeviationThresholdMm { get; set; } = 1.0;

    /// <summary>InstrumentCalibrationTask 暫存有效分鐘數（預設 30）。</summary>
    public int InstrumentTaskTtlMinutes { get; set; } = 30;

    /// <summary>
    /// 測試模式：不連掃描器、不寫補償／Ref，直接回固定範例供串接。
    /// </summary>
    public bool TestMode { get; set; }

    /// <summary>是否為除錯回應模式。</summary>
    public bool IsDebugResponse
    {
        get
        {
            return string.Equals(ResponseMode, "Debug", StringComparison.OrdinalIgnoreCase);
        }
    }
}
