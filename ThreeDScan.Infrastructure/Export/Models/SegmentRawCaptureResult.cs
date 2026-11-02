using ThreeDScan.Domain.Models;

namespace ThreeDScan.Infrastructure.Export.Models;

/// <summary>段 raw PLY 擷取結果（路徑、重播旗標、capture 中繼）。</summary>
public sealed class SegmentRawCaptureResult
{
    /// <summary>raw.ply 路徑。</summary>
    public string RawPlyPath { get; init; } = "";

    /// <summary>本次是否為重播／離線檔（Hank capture.IsReplay）。</summary>
    public bool IsReplay { get; init; }

    /// <summary>對外 JSON capture；重播時可為 null。</summary>
    public SegmentCaptureMetadata? Metadata { get; init; }
}
