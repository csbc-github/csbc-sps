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

    /// <summary>讀取進料區框選。沒有框選則為 null。</summary>
    LineScanRegion? GetLineRegion();

    /// <summary>寫入或清除進料區框選。null 表示清除。</summary>
    void SetLineRegion(LineScanRegion? region);

    /// <summary>進行中的錄製與最新一圈截面。沒有錄製時 Recording 為 false。</summary>
    LineScanLiveFrame GetLiveFrame();

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
