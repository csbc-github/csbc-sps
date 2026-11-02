using System.Text.Json.Serialization;
using ThreeDScan.Application.Common.Models;
using ThreeDScan.Domain.Models;

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

    /// <summary>規格 v1.1 未列，由 Hank 搬入。固定 ConfiguredAbsolute。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CoordinateFrame { get; init; }

    /// <summary>規格 v1.1 未列，由 Hank 搬入。是否已現場校正。</summary>
    public bool AbsoluteCoordinatesCalibrated { get; init; }

    /// <summary>規格 v1.1 未列，由 Hank 搬入。補償 TraceId。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CalibrationId { get; init; }

    /// <summary>規格 v1.1 未列，由 Hank 搬入。段序。</summary>
    public int SegmentNo { get; init; }

    /// <summary>規格 v1.1 未列，由 Hank 搬入。本段拍攝完成時間。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? CapturedAt { get; init; }

    /// <summary>規格 v1.1 未列，由 Hank 搬入。警告。</summary>
    public IReadOnlyList<string>? Warnings { get; init; }

    /// <summary>規格 v1.1 未列，由 Hank 搬入。是否重播 ply。</summary>
    public bool IsReplay { get; init; }

    /// <summary>規格 v1.1 未列，由 Hank 搬入。本次拍攝中繼資料。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SegmentCaptureMetadata? Capture { get; init; }

    /// <summary>規格 v1.1 未列，由 Hank 搬入。段品質摘要。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SurfaceScanSegmentQuality? Quality { get; init; }
}
