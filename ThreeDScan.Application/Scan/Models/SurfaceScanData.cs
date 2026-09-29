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

    /// <summary>本段已補償之完整點雲下載位置；每次呼叫皆回傳。多段且 <c>final=true</c> 時為拼接後點雲。</summary>
    public string PointCloudUrl { get; init; } = "";

    /// <summary>本次是否套用 UC-01 補償。false 表示沒有補償歷程，公開檔只加了 cameraPosition。</summary>
    public bool CompensationApplied { get; init; }

    /// <summary>左側厚度（mm）；本次未帶 innerLeft 時省略。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("thicknessLeft")]
    public double? ThicknessLeft { get; init; }

    /// <summary>右側厚度（mm）；本次未帶 innerRight 時省略。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("thicknessRight")]
    public double? ThicknessRight { get; init; }

    /// <summary>本段沿移動方向包角線最大 Z 差（mm）；僅數值，不含告警。</summary>
    public double SegmentZDelta { get; init; }

    /// <summary>本段前緣左下／頂點／右下（公開絕對座標：相對 + cameraPosition + 補償）。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("headFeatures")]
    public CornerTripletDto? HeadFeatures { get; init; }

    /// <summary><c>angle</c> 倒 V；<c>envelope</c> 非角鋼包絡。無三點時省略。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("headFeaturesKind")]
    public string? HeadFeaturesKind { get; init; }

    /// <summary>本次呼叫追溯 ID。</summary>
    public string TraceId { get; init; } = "";
}
