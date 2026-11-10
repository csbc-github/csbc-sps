namespace ThreeDScan.Infrastructure.LineScan;

/// <summary>線掃描裝置層錯誤（對應 Hank ScanApiException 語意）。</summary>
public sealed class LineScanDeviceException : Exception
{
    /// <summary>建立裝置錯誤。</summary>
    public LineScanDeviceException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    /// <summary>業務錯誤碼。</summary>
    public string Code { get; }
}
