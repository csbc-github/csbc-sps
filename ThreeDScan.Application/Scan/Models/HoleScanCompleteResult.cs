using ThreeDScan.Application.Common.Models;

namespace ThreeDScan.Application.Scan.Models;

/// <summary>孔洞掃描最終 result（callback／GET 查詢）。</summary>
public sealed class HoleScanCompleteResult
{
    /// <summary>本次掃描識別碼。</summary>
    public string ScanId { get; init; } = "";

    /// <summary>掃描完成時間（ISO 8601）。</summary>
    public string CapturedAt { get; init; } = "";

    /// <summary>孔洞或邊緣缺口之邊界座標序列。</summary>
    public IReadOnlyList<CoordinateDto> FeatureBoundary { get; init; } = [];
}
