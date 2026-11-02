namespace ThreeDScan.Domain.Models;

/// <summary>任務儲存／查找失敗。對應 Hank <c>Scan3D.Service.ApiException</c> 的任務相關錯誤。</summary>
public sealed class ScanTaskException : Exception
{
    /// <summary>建立任務例外。</summary>
    /// <param name="statusCode">HTTP 狀態。</param>
    /// <param name="code">業務錯誤碼。</param>
    /// <param name="message">訊息。</param>
    /// <param name="details">可選細節。</param>
    public ScanTaskException(int statusCode, string code, string message, object? details = null)
        : base(message)
    {
        StatusCode = statusCode;
        Code = code;
        Details = details;
    }

    /// <summary>HTTP 狀態。</summary>
    public int StatusCode { get; }

    /// <summary>業務錯誤碼。</summary>
    public string Code { get; }

    /// <summary>可選細節。</summary>
    public object? Details { get; }
}
