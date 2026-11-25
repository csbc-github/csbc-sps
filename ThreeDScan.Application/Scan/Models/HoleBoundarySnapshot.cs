using ThreeDScan.Application.Common.Models;

namespace ThreeDScan.Application.Scan.Models;

/// <summary>監看檔解析後的孔洞邊界與點雲路徑。</summary>
/// <remarks>
/// <see cref="Points"/> 為公開絕對座標（右側座標表）；
/// <see cref="RawPlyPath"/> 為掃描器座標 PLY（左側 3D 點雲，與噴印區載入方式相同）。
/// </remarks>
public sealed class HoleBoundarySnapshot
{
    /// <summary>邊界點（公開絕對座標 mm）。</summary>
    public IReadOnlyList<CoordinateDto> Points { get; init; } = Array.Empty<CoordinateDto>();

    /// <summary>本次擷取的 raw PLY 絕對路徑（掃描器座標）；供 WPF 3D 點雲顯示。</summary>
    public string? RawPlyPath { get; init; }
}
