using System.Text.Json.Serialization;

namespace ThreeDScan.Application.Scan.Models;

/// <summary>面掃描結果：任務狀態、點雲位置與可選厚度。</summary>
public sealed class SurfaceScanData
{
    /// <summary>尚未結束，可再呼叫。</summary>
    public const string StatusContinue = "Continue";

    /// <summary>已完成，回傳完整結果。</summary>
    public const string StatusFinal = "Final";

    /// <summary>本次掃描任務識別碼。</summary>
    public string TaskId { get; init; } = "";

    /// <summary>目前呼叫次數。</summary>
    public int Seq { get; init; }

    /// <summary><see cref="StatusContinue"/> 或 <see cref="StatusFinal"/>。</summary>
    public string Status { get; init; } = "";

    /// <summary>本段已補償之完整點雲下載位置；每次呼叫皆回傳。</summary>
    public string PointCloudUrl { get; init; } = "";

    /// <summary>厚度量測結果；<c>measureThickness=false</c> 時為 null。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Thickness { get; init; }

    /// <summary>與前一段的 Z 高度差（斜率）；僅數值，不含告警。</summary>
    public double SegmentZDelta { get; init; }

    /// <summary>本次呼叫追溯 ID。</summary>
    public string TraceId { get; init; } = "";
}
