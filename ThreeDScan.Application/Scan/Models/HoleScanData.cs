using ThreeDScan.Application.Calibration.Models;

namespace ThreeDScan.Application.Scan.Models;

/// <summary>孔洞掃描結果：邊界座標序列。</summary>
public sealed class HoleScanData
{
    /// <summary>本次孔洞掃描識別碼。</summary>
    public string ScanId { get; init; } = "";

    /// <summary>掃描完成時間（ISO 8601）。</summary>
    public string CapturedAt { get; init; } = "";

    /// <summary>孔洞邊界座標序列（原生掃描精度，約 0.2 mm）。</summary>
    public IReadOnlyList<CoordinateDto> HoleFeatures { get; init; } = [];

    /// <summary>本次呼叫追溯 ID。</summary>
    public string TraceId { get; init; } = "";
}
