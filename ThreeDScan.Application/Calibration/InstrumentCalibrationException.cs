namespace ThreeDScan.Application.Calibration;

/// <summary>儀器校正業務例外，帶 SD 錯誤代碼。</summary>
public sealed class InstrumentCalibrationException : Exception
{
    /// <summary>
    /// 建立校正例外。
    /// </summary>
    /// <param name="errorCode">API 錯誤代碼（如 CAL_FEED_001）。</param>
    /// <param name="message">錯誤訊息。</param>
    public InstrumentCalibrationException(string errorCode, string message) : base(message)
    {
        ErrorCode = errorCode;
    }

    /// <summary>API 錯誤代碼。</summary>
    public string ErrorCode { get; }
}
