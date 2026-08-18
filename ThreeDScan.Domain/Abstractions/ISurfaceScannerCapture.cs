namespace ThreeDScan.Domain.Abstractions;

/// <summary>Keyence LJS8 面掃描器單次擷取。</summary>
public interface ISurfaceScannerCapture
{
    /// <summary>此擷取服務對應的 ScannerId。</summary>
    string ScannerId { get; }

    /// <summary>
    /// 校正／量測用：只擷取高度／灰階影像（不跑 Python／角鋼後處理）。
    /// </summary>
    Task<ScannerCaptureResult> CaptureImagesAsync(byte programNo, CancellationToken ct = default);

    /// <summary>
    /// UC-04 面掃描：影像 + raw PLY → Python 角鋼抽出 → ZIP／GLB／角度。
    /// </summary>
    Task<ScannerCaptureResult> CaptureWithAngleSteelAsync(byte programNo, CancellationToken ct = default);
}

/// <summary>單次掃描擷取結果（匯出檔路徑與影像幾何）。</summary>
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

    public Models.CaptureImageGeometry? Geometry { get; init; }
}
