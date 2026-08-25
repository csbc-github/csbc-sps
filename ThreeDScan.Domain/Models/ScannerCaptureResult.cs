namespace ThreeDScan.Domain.Models;

/// <summary>單次掃描擷取結果（暫存檔路徑與影像幾何）。</summary>
public sealed class ScannerCaptureResult
{
    public required string CaptureId { get; init; }
    public string? HeightImagePath { get; init; }
    public string? GrayImagePath { get; init; }

    /// <summary>去地／去雜訊後最大點群（角鋼）ASCII PLY，單位 mm。</summary>
    public string? PlyPath { get; init; }

    /// <summary>角鋼 PLY 點數。</summary>
    public int PlyPointCount { get; init; }

    /// <summary>Hank Pipeline ZIP（PLY/JSON/STEP/GLB/PNG）。</summary>
    public string? ZipPath { get; init; }

    /// <summary>自 ZIP 抽出的角鋼網格 GLB（供 WPF 顯示）。</summary>
    public string? GlbPath { get; init; }

    /// <summary>合併座標 JSON。</summary>
    public string? JsonPath { get; init; }

    /// <summary>兩翼板夾角（度）；無法估算時為 null。</summary>
    public double? AngleDegrees { get; init; }

    /// <summary>偏離 90° 的角度（度）。</summary>
    public double? AngleDeviationFromSquareDegrees { get; init; }

    public CaptureImageGeometry? Geometry { get; init; }
}
