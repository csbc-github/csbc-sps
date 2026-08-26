using ThreeDScan.Domain.Models;

namespace ThreeDScan.Domain.Abstractions;

/// <summary>
/// 將單次面掃描擷取轉成 C# Pipeline 匯出（raw PLY → 去地／去雜訊 → ZIP／PLY／JSON／GLB）。
/// </summary>
public interface ISurfaceScanPipeline
{
    /// <summary>
    /// 由高度圖寫 ASCII raw PLY，濾點後匯出。
    /// 有儀器補償歷程時，公開點雲／ZIP／GLB 為套 offset 後的結果。
    /// </summary>
    /// <param name="capture">本次擷取（須含高度圖路徑與 CaptureId 快取）。</param>
    /// <param name="ct">取消權杖。</param>
    /// <param name="thickness">夾具 A／B；有值時量測兩翼厚度並組 STEP／GLB。</param>
    /// <param name="storedThickness">已存的兩翼厚度；有值時用存檔厚度組網格。</param>
    /// <returns>點雲／ZIP 路徑、可選的量測厚度與夾角。</returns>
    Task<SurfaceScanPipelineResult> ProcessCaptureAsync(
        ScannerCaptureResult capture,
        CancellationToken ct = default,
        ThicknessMeasureRequest? thickness = null,
        JobThicknessRecord? storedThickness = null);
}
