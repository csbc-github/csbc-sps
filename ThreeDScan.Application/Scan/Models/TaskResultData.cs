using ThreeDScan.Application.Common.Models;

namespace ThreeDScan.Application.Scan.Models;

/// <summary>GET /api/tasks/{taskId}/result 與 callback 共用形狀。</summary>
public sealed class TaskResultData
{
    /// <summary>處理中。</summary>
    public const string StatusProcessing = "Processing";

    /// <summary>成功。</summary>
    public const string StatusCompleted = "Completed";

    /// <summary>失敗。</summary>
    public const string StatusFailed = "Failed";

    /// <summary>已中止。</summary>
    public const string StatusAborted = "Aborted";

    /// <summary>任務識別碼。</summary>
    public string TaskId { get; init; } = "";

    /// <summary>Processing／Completed／Failed／Aborted。</summary>
    public string Status { get; init; } = "";

    /// <summary>僅 Completed 時提供；內容依任務種類而異。</summary>
    public object? Result { get; init; }

    /// <summary>失敗時的錯誤；成功為 null。</summary>
    public TaskErrorDto? Error { get; init; }

    /// <summary>本次任務追溯 ID。</summary>
    public string TraceId { get; init; } = "";
}
