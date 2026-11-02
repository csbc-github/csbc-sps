using ThreeDScan.Application.Common.Models;

namespace ThreeDScan.Application.Scan.Models;

/// <summary>監看檔解析後的孔洞邊界座標。</summary>
public sealed class HoleBoundarySnapshot
{
    /// <summary>邊界點（掃描器座標 mm）。</summary>
    public IReadOnlyList<CoordinateDto> Points { get; init; } = Array.Empty<CoordinateDto>();
}
