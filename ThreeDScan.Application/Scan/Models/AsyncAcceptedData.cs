namespace ThreeDScan.Application.Scan.Models;

/// <summary>HTTP 202 立即回應：已接受、處理中。</summary>
public sealed class AsyncAcceptedData
{
    /// <summary>處理中。</summary>
    public const string StatusProcessing = "Processing";

    /// <summary>任務識別碼。</summary>
    public string TaskId { get; init; } = "";

    /// <summary>固定 <see cref="StatusProcessing"/>。</summary>
    public string Status { get; init; } = StatusProcessing;
}
