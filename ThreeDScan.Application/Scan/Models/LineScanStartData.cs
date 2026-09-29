namespace ThreeDScan.Application.Scan.Models;

/// <summary>POST /api/scan/line/start 成功回應。</summary>
public sealed class LineScanStartData
{
    /// <summary>本次掃描任務識別碼，後續呼叫須帶回。</summary>
    public string TaskId { get; init; } = "";

    /// <summary>開始時間（ISO 8601）。</summary>
    public string StartedAt { get; init; } = "";

    /// <summary>本次呼叫追溯 ID。</summary>
    public string TraceId { get; init; } = "";
}
