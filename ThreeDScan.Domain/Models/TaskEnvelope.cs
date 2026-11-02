using System.Text.Json;

namespace ThreeDScan.Domain.Models;

/// <summary>任務對外信封。對應 Hank <c>TaskRepository.Envelope</c>。</summary>
public sealed class TaskEnvelope
{
    /// <summary>任務編號。</summary>
    public string TaskId { get; init; } = "";

    /// <summary>狀態。</summary>
    public string Status { get; init; } = "";

    /// <summary>完成時的結果。</summary>
    public JsonElement? Result { get; init; }

    /// <summary>失敗內容。</summary>
    public TaskApiError? Error { get; init; }

    /// <summary>追溯 ID。</summary>
    public string TraceId { get; init; } = "";

    /// <summary>階段。</summary>
    public string Phase { get; init; } = "";

    /// <summary>是否重播。</summary>
    public bool IsReplay { get; init; }

    /// <summary>建立時刻。</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>最後更新時刻。</summary>
    public DateTimeOffset UpdatedAt { get; init; }

    /// <summary>回呼狀態。</summary>
    public TaskCallbackEnvelope Callback { get; init; } = new TaskCallbackEnvelope();
}
