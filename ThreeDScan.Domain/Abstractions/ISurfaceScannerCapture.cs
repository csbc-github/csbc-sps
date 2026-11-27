using ThreeDScan.Domain.Models;

namespace ThreeDScan.Domain.Abstractions;

/// <summary>Keyence LJS8 面掃描器單次擷取（灰階／高度圖）。</summary>
public interface ISurfaceScannerCapture
{
    /// <summary>此擷取服務對應的 ScannerId。</summary>
    string ScannerId { get; }

    /// <summary>切換至指定 programNo 並擷取一幀掃描影像（校正／右上角測試掃描，不跑角鋼後處理）。</summary>
    Task<ScannerCaptureResult> CaptureAsync(byte programNo, CancellationToken ct = default);

    /// <summary>UC-04 面掃描：影像 + raw PLY → C# Pipeline.Run → ZIP／GLB／角度（與 API 同一條）。</summary>
    Task<ScannerCaptureResult> CaptureWithAngleSteelAsync(byte programNo, CancellationToken ct = default);

    /// <summary>孔洞用 OneClick 擷取：最多 1000 列、保留原始高度、XY 區域格網；null 保持目前程式。</summary>
    Task<HoleRawCaptureResult> CaptureHoleAsync(byte? programNo = null, CancellationToken ct = default)
        => throw new NotSupportedException("此掃描器未提供 OneClick 孔洞擷取。");
}
