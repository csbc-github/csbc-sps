namespace ThreeDScan.Application.Scan.Models;

/// <summary>線／面掃描 abort 成功回應。</summary>
public sealed class ScanAbortData
{
    /// <summary>已中止。</summary>
    public const string StatusAborted = "Aborted";

    /// <summary>已中斷之任務識別碼。</summary>
    public string TaskId { get; init; } = "";

    /// <summary>固定 <see cref="StatusAborted"/>。</summary>
    public string Status { get; init; } = StatusAborted;

    /// <summary>本次呼叫追溯 ID。</summary>
    public string TraceId { get; init; } = "";
}
