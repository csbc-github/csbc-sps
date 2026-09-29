using ThreeDScan.Application.Common.Models;
using ThreeDScan.Application.Scan.Models;

namespace ThreeDScan.Application.Scan;

/// <summary>線掃描、面掃描與孔洞掃描。</summary>
public interface IScanAppService
{
    /// <summary>預備進料區線掃描：滾輪傳動時持續辨識角鋼輪廓。</summary>
    Task<ApiResponse<LineScanData>> ScanLineAsync(LineScanRequest request, CancellationToken ct = default);

    /// <summary>指南 POST /api/scan/line/start。</summary>
    Task<DasOperationResult<LineScanStartData>> StartLineScanAsync(
        LineScanRequest request,
        CancellationToken ct = default);

    /// <summary>指南 POST /api/scan/line/complete。</summary>
    Task<DasOperationResult<AsyncAcceptedData>> CompleteLineScanAsync(
        LineScanCompleteRequest request,
        CancellationToken ct = default);

    /// <summary>指南 POST /api/scan/line/abort。</summary>
    Task<DasOperationResult<ScanAbortData>> AbortLineScanAsync(
        LineScanAbortRequest request,
        CancellationToken ct = default);

    /// <summary>面掃描。</summary>
    Task<ApiResponse<SurfaceScanData>> ScanSurfaceAsync(SurfaceScanRequest request, CancellationToken ct = default);

    /// <summary>略過掃描器擷取，用既有 raw_scene.ply 跑與面掃描相同的後續處理。</summary>
    Task<ApiResponse<SurfaceScanData>> ScanSurfaceFromFileAsync(
        SurfaceFromFileRequest request,
        CancellationToken ct = default);

    /// <summary>指南 POST /api/scan/surface/start。</summary>
    Task<DasOperationResult<SurfaceScanStartData>> StartSurfaceScanAsync(
        SurfaceScanStartRequest request,
        CancellationToken ct = default);

    /// <summary>指南 POST /api/scan/surface/segment。</summary>
    Task<DasOperationResult<SurfaceScanSegmentData>> SegmentSurfaceScanAsync(
        SurfaceScanSegmentRequest request,
        CancellationToken ct = default);

    /// <summary>指南 POST /api/scan/surface/complete。</summary>
    Task<DasOperationResult<AsyncAcceptedData>> CompleteSurfaceScanAsync(
        SurfaceScanCompleteRequest request,
        CancellationToken ct = default);

    /// <summary>背景拼接、寫結果並 POST callback。</summary>
    Task RunSurfaceCompleteJobAsync(string taskId, string callbackUrl, CancellationToken ct = default);

    /// <summary>指南 POST /api/scan/surface/abort。</summary>
    Task<DasOperationResult<ScanAbortData>> AbortSurfaceScanAsync(
        SurfaceScanAbortRequest request,
        CancellationToken ct = default);

    /// <summary>下載面掃描附加檔（ZIP：STEP／glTF／PNG）。</summary>
    Task<ApiResponse<SurfaceScanAttachmentFile>> GetSurfaceAttachmentsAsync(
        string taskId,
        CancellationToken ct = default);

    /// <summary>研磨區孔洞掃描，回傳孔洞邊界座標。</summary>
    Task<ApiResponse<HoleScanData>> ScanHoleAsync(HoleScanRequest request, CancellationToken ct = default);

    /// <summary>指南 GET /api/tasks/{taskId}/result。</summary>
    Task<DasOperationResult<TaskResultData>> GetTaskResultAsync(
        string taskId,
        CancellationToken ct = default);
}
