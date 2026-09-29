using ThreeDScan.Application.Common.Models;

namespace ThreeDScan.Application.Scan.Models;

/// <summary>POST /api/scan/surface/segment 成功回應（無厚度）。</summary>
public sealed class SurfaceScanSegmentData
{
    /// <summary>本段已轉換之點雲下載位置。</summary>
    public string PointCloudUrl { get; init; } = "";

    /// <summary>本段前緣左下／頂點／右下。</summary>
    public CornerTripletDto HeadFeatures { get; init; } = new();

    /// <summary>本段沿移動方向頭尾 Z 值差 (mm)。</summary>
    public double SegmentZDelta { get; init; }

    /// <summary>本次呼叫追溯 ID。</summary>
    public string TraceId { get; init; } = "";
}
