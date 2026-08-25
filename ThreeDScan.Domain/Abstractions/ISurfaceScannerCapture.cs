using ThreeDScan.Domain.Models;

namespace ThreeDScan.Domain.Abstractions;

/// <summary>Keyence LJS8 面掃描器單次擷取（灰階／高度圖）。</summary>
public interface ISurfaceScannerCapture
{
    /// <summary>此擷取服務對應的 ScannerId。</summary>
    string ScannerId { get; }

    /// <summary>切換至指定 programNo 並擷取一幀掃描影像（校正／量測，不跑角鋼後處理）。</summary>
    Task<ScannerCaptureResult> CaptureAsync(byte programNo, CancellationToken ct = default);

    /// <summary>UC-04 面掃描：影像 + raw PLY → Python 角鋼抽出 → ZIP／GLB／角度。</summary>
    Task<ScannerCaptureResult> CaptureWithAngleSteelAsync(byte programNo, CancellationToken ct = default);
}
