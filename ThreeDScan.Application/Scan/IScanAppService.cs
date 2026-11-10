using ThreeDScan.Application.Common.Models;
using ThreeDScan.Application.Scan.Models;
using ThreeDScan.Domain.Models;

namespace ThreeDScan.Application.Scan;

/// <summary>線掃描、面掃描與孔洞掃描。</summary>
public interface IScanAppService
{
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

    /// <summary>最近一次線掃描 complete 的結果；尚無結果時狀態為 None。</summary>
    TaskResultData GetLatestLineScanResult();

    // v1 legacy line/region（API 已 DEPRECATED；ROI 改 api/scanners/{id}/roi）
    // LineScanRegion? GetLineRegion();
    // void SetLineRegion(LineScanRegion? region);

    /// <summary>進行中的錄製與最新一圈截面；未錄製時讀架設預覽快取（O8）。</summary>
    Task<LineScanLiveFrame> GetLiveFrameAsync(CancellationToken ct = default);

    /// <summary>開始架設長連預覽。</summary>
    Task<DasOperationResult<object>> StartLineSetupPreviewAsync(
        LineScanSetupRequest request,
        CancellationToken ct = default);

    /// <summary>結束架設預覽。</summary>
    Task<DasOperationResult<object>> StopLineSetupPreviewAsync(
        LineScanSetupRequest request,
        CancellationToken ct = default);

    /// <summary>凍結一帧完整輪廓（POST /api/scan/profile）。</summary>
    Task<DasOperationResult<LineScanProfileData>> CaptureLineProfileAsync(
        LineScanProfileRequest request,
        CancellationToken ct = default);

    /// <summary>背景停止錄製、整理角鋼並 POST callback。</summary>
    Task RunLineCompleteJobAsync(string taskId, string callbackUrl, CancellationToken ct = default);

    /// <summary>Watchdog：中斷錄製並刪除任務，之後查詢視為不存在。</summary>
    Task ExpireLineScanAsync(string taskId, CancellationToken ct = default);

    /// <summary>面掃描。</summary>
    Task<ApiResponse<SurfaceScanData>> ScanSurfaceAsync(SurfaceScanRequest request, CancellationToken ct = default);

    /// <summary>略過掃描器擷取，用既有 raw_scene.ply 跑與面掃描相同的後續處理。</summary>
    Task<ApiResponse<SurfaceScanData>> ScanSurfaceFromFileAsync(
        SurfaceFromFileRequest request,
        CancellationToken ct = default);

    /// <summary>指南 POST /api/scan/surface/start。</summary>
    /// <remarks>來自 Hank Scan3D 的 <c>Scan3D.Service.ScanOrchestrator</c>。</remarks>
    Task<DasOperationResult<SurfaceScanStartData>> StartSurfaceScanAsync(
        SurfaceScanStartRequest request,
        string traceId,
        CancellationToken ct = default,
        string? baseUrl = null);

    /// <summary>指南 POST /api/scan/surface/segment。</summary>
    /// <remarks>來自 Hank Scan3D 的 <c>Scan3D.Service.ScanOrchestrator</c>。</remarks>
    Task<DasOperationResult<SurfaceScanSegmentData>> SegmentSurfaceScanAsync(
        SurfaceScanSegmentRequest request,
        string traceId,
        string? idempotencyKeyHeader,
        CancellationToken ct = default);

    /// <summary>指南 POST /api/scan/surface/complete。</summary>
    /// <remarks>來自 Hank Scan3D 的 <c>Scan3D.Service.ScanOrchestrator</c>。</remarks>
    Task<DasOperationResult<AsyncAcceptedData>> CompleteSurfaceScanAsync(
        SurfaceScanCompleteRequest request,
        string traceId,
        CancellationToken ct = default);

    /// <summary>背景拼接、寫結果並 POST callback。</summary>
    Task RunSurfaceCompleteJobAsync(string taskId, string callbackUrl, CancellationToken ct = default);

    /// <summary>指南 POST /api/scan/surface/abort。</summary>
    /// <remarks>來自 Hank Scan3D 的 <c>Scan3D.Service.ScanOrchestrator</c>。</remarks>
    Task<DasOperationResult<ScanAbortData>> AbortSurfaceScanAsync(
        SurfaceScanAbortRequest request,
        CancellationToken ct = default);

    /// <summary>下載面掃描附加檔（ZIP：STEP／glTF／PNG）。</summary>
    /// <remarks>來自 Hank Scan3D 的 <c>Scan3D.Service.ArtifactService</c>。</remarks>
    Task<ApiResponse<SurfaceScanAttachmentFile>> GetSurfaceAttachmentsAsync(
        string taskId,
        CancellationToken ct = default);

    /// <summary>研磨區孔洞掃描（舊同步契約；正式路徑請用 <see cref="StartHoleScanAsync"/>）。</summary>
    Task<ApiResponse<HoleScanData>> ScanHoleAsync(HoleScanRequest request, CancellationToken ct = default);

    /// <summary>指南 POST /api/scan/hole。立即 202。</summary>
    /// <remarks>來自 Hank Scan3D 的 <c>Scan3D.Service.ScanOrchestrator</c>。</remarks>
    Task<DasOperationResult<AsyncAcceptedData>> StartHoleScanAsync(
        HoleScanRequest request,
        string baseUrl,
        CancellationToken ct = default);

    /// <summary>指南 GET /api/tasks/{taskId}/result。</summary>
    Task<DasOperationResult<TaskResultData>> GetTaskResultAsync(
        string taskId,
        CancellationToken ct = default);
}
