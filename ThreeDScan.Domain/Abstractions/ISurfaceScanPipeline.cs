using ThreeDScan.Domain.Models;

namespace ThreeDScan.Domain.Abstractions;

/// <summary>將單次面掃描擷取轉成 C# Pipeline.Run 匯出（不經 Python exe）。</summary>
public interface ISurfaceScanPipeline
{
    /// <summary>由高度圖寫 raw PLY，再跑地面濾除、分群與 ZIP／PLY 匯出。</summary>
    Task<SurfaceScanPipelineResult> ProcessCaptureAsync(
        ScannerCaptureResult capture,
        CancellationToken ct = default);
}
