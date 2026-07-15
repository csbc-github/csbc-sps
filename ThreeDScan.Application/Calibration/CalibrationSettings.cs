namespace ThreeDScan.Application.Calibration;

/// <summary>校正 API 行為設定（appsettings → Calibration）。</summary>
public sealed class CalibrationSettings
{
    /// <summary>
    /// 回應模式：<c>Production</c>（正式，僅補償等必要欄位）或 <c>Debug</c>（含診斷欄位）。
    /// </summary>
    public string ResponseMode { get; set; } = "Production";

    /// <summary>寫入 OperationLog 的呼叫端識別（預設台船）。</summary>
    public string ClientId { get; set; } = "台船";

    /// <summary>是否為除錯回應模式。</summary>
    public bool IsDebugResponse =>
        string.Equals(ResponseMode, "Debug", StringComparison.OrdinalIgnoreCase);
}
