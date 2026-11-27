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

    /// <summary>可選的物件顯示點雲；仍須保留掃描器 XYZ。</summary>
    public string? ExtractedPlyPath { get; init; }

    /// <summary>本次量測的所有開口；各孔邊界獨立保存。</summary>
    public IReadOnlyList<HoleFeatureSnapshot> Features { get; init; } = Array.Empty<HoleFeatureSnapshot>();

    public string? SelectedFeatureId { get; init; }

    public string? TaskId { get; init; }

    public DateTimeOffset? CapturedAt { get; init; }

    public string? CoordinateFrame { get; init; }

    public bool AngleSteelOnly { get; init; }
    public bool WorkpieceIdentified { get; init; }
    public string? ScopeReason { get; init; }

    /// <summary>null 表示舊版監看資料未提供驗證狀態。</summary>
    public bool? AbsoluteCoordinatesCalibrated { get; init; }

    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}

/// <summary>一個開口的公開座標與原始掃描器座標，兩者不可互相替代。</summary>
public sealed class HoleFeatureSnapshot
{
    public string Id { get; init; } = "";
    public string FaceId { get; init; } = "";
    public string Type { get; init; } = "Unknown";
    public string Status { get; init; } = "PARTIAL";
    public bool Closed { get; init; }
    public double? WidthMm { get; init; }
    public double? HeightMm { get; init; }
    public string? Reason { get; init; }
    public IReadOnlyList<CoordinateDto> Boundary { get; init; } = Array.Empty<CoordinateDto>();
    public IReadOnlyList<CoordinateDto> ScannerBoundary { get; init; } = Array.Empty<CoordinateDto>();
}
