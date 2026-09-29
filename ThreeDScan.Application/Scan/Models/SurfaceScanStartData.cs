using System.Text.Json.Serialization;
using ThreeDScan.Application.Common.Models;

namespace ThreeDScan.Application.Scan.Models;

/// <summary>POST /api/scan/surface/start 成功回應。</summary>
public sealed class SurfaceScanStartData
{
    /// <summary>本次掃描任務識別碼，後續呼叫須帶回。</summary>
    public string TaskId { get; init; } = "";

    /// <summary>本段（第一段）已轉換之點雲下載位置。</summary>
    public string PointCloudUrl { get; init; } = "";

    /// <summary>本段前緣左下／頂點／右下。</summary>
    public CornerTripletDto HeadFeatures { get; init; } = new();

    /// <summary>本段沿移動方向頭尾 Z 值差 (mm)。</summary>
    public double SegmentZDelta { get; init; }

    /// <summary>左側厚度；未帶 innerLeft 或無法計算時為 null。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? ThicknessLeft { get; init; }

    /// <summary>右側厚度；未帶 innerRight 或無法計算時為 null。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? ThicknessRight { get; init; }

    /// <summary>本次呼叫追溯 ID。</summary>
    public string TraceId { get; init; } = "";
}
