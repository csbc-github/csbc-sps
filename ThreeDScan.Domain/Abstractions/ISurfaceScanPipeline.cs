using ThreeDScan.Domain.Models;

namespace ThreeDScan.Domain.Abstractions;

/// <summary>
/// 將單次面掃描擷取轉成 C# Pipeline 匯出（raw PLY → 去地／去雜訊 → ZIP／PLY／JSON／GLB）。
/// </summary>
public interface ISurfaceScanPipeline
{
    /// <summary>
    /// 由高度圖寫 ASCII raw PLY，濾點後匯出。
    /// 公開點雲／ZIP／GLB 為相對座標加上 <paramref name="cameraPosition"/> 與儀器補償。
    /// </summary>
    /// <param name="capture">本次擷取（須含高度圖路徑與 CaptureId 快取）。</param>
    /// <param name="ct">取消權杖。</param>
    /// <param name="thickness">夾具 A／B；有值時量測兩翼厚度並組 STEP／GLB。</param>
    /// <param name="storedThickness">已存的兩翼厚度；有值時用存檔厚度組網格。</param>
    /// <param name="cameraPosition">DAS 期望掃描器到位座標；null 視為 (0,0,0)。</param>
    /// <returns>點雲／ZIP 路徑、可選的量測厚度與夾角。</returns>
    Task<SurfaceScanPipelineResult> ProcessCaptureAsync(
        ScannerCaptureResult capture,
        CancellationToken ct = default,
        ThicknessMeasureRequest? thickness = null,
        JobThicknessRecord? storedThickness = null,
        CameraPosition? cameraPosition = null);

    /// <summary>
    /// 將多段已處理點雲拼接後匯出 ZIP／PLY／JSON／GLB。
    /// </summary>
    /// <param name="segmentPlyPaths">各段公開點雲 PLY，順序即沿 X 排列。</param>
    /// <param name="outputDirectory">拼接結果寫入此目錄。</param>
    /// <param name="ct">取消權杖。</param>
    /// <param name="storedThickness">已存兩翼厚度；有值時用存檔厚度組網格。</param>
    /// <returns>拼接後的點雲／ZIP 路徑。</returns>
    Task<SurfaceScanPipelineResult> ExportStitchedAsync(
        IReadOnlyList<string> segmentPlyPaths,
        string outputDirectory,
        CancellationToken ct = default,
        JobThicknessRecord? storedThickness = null);

    /// <summary>
    /// 略過掃描器與高度圖，直接用既有 raw PLY 跑與 <see cref="ProcessCaptureAsync"/> 相同的後續匯出。
    /// </summary>
    /// <param name="scannerId">掃描器編號，用來讀最新儀器補償。</param>
    /// <param name="rawPlyPath">raw_scene.ply 絕對路徑。</param>
    /// <param name="outputDirectory">ZIP／PLY／JSON／GLB 寫入此目錄。</param>
    /// <param name="ct">取消權杖。</param>
    /// <param name="thickness">夾具 A／B；有值時量測兩翼厚度並組 STEP／GLB。</param>
    /// <param name="storedThickness">已存的兩翼厚度；有值時用存檔厚度組網格。</param>
    /// <param name="cameraPosition">DAS 期望掃描器到位座標；null 視為 (0,0,0)。</param>
    /// <returns>點雲／ZIP 路徑、可選的量測厚度與夾角。</returns>
    Task<SurfaceScanPipelineResult> ProcessRawPlyAsync(
        string scannerId,
        string rawPlyPath,
        string outputDirectory,
        CancellationToken ct = default,
        ThicknessMeasureRequest? thickness = null,
        JobThicknessRecord? storedThickness = null,
        CameraPosition? cameraPosition = null);
}
