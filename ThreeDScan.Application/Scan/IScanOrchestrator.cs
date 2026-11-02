using ThreeDScan.Domain.Models;

namespace ThreeDScan.Application.Scan;

/// <summary>面掃描／孔洞正式路徑。</summary>
/// <remarks>由 Hank Scan3D 的 ScanOrchestrator Surface／Hole 搬入，邏輯不改。</remarks>
public interface IScanOrchestrator
{
    /// <summary>面掃描第一段。</summary>
    /// <remarks>來自 Hank Scan3D 的 <c>Scan3D.Service.ScanOrchestrator</c>。</remarks>
    Task<ScanOutcome> StartSurfaceAsync(
        string scannerId,
        string jobId,
        Pose5 cameraPosition,
        int? navigatorScanNo,
        Point3? innerLeft,
        Point3? innerRight,
        double? materialThicknessMm,
        ScannerRoi? roi,
        int? componentRank,
        StitchMode stitchMode,
        double nominalGapMm,
        string traceId,
        string baseUrl,
        CancellationToken ct);

    /// <summary>面掃描後續段。</summary>
    /// <remarks>來自 Hank Scan3D 的 <c>Scan3D.Service.ScanOrchestrator</c>。</remarks>
    Task<ScanOutcome> SegmentSurfaceAsync(
        string taskId,
        Pose5 cameraPosition,
        int? navigatorScanNo,
        ScannerRoi? roi,
        int? componentRank,
        string? segmentKey,
        string traceId,
        CancellationToken ct);

    /// <summary>結束面掃描並排入拼接。</summary>
    /// <remarks>來自 Hank Scan3D 的 <c>Scan3D.Service.ScanOrchestrator</c>。</remarks>
    Task<ScanOutcome> CompleteSurfaceAsync(string taskId, string callbackUrl, string traceId, CancellationToken ct);

    /// <summary>背景拼接。</summary>
    /// <remarks>來自 Hank Scan3D 的 <c>Scan3D.Service.ScanOrchestrator</c>。</remarks>
    Task ExecuteSurfaceAsync(string taskId, CancellationToken ct);

    /// <summary>開始孔洞並排入拍攝／辨識。</summary>
    /// <remarks>來自 Hank Scan3D 的 <c>Scan3D.Service.ScanOrchestrator</c>。</remarks>
    Task<ScanOutcome> StartHoleAsync(
        string scannerId,
        string jobId,
        string callbackUrl,
        string? holeType,
        Pose5? cameraPosition,
        ScannerRoi? roi,
        string? featureId,
        int? navigatorScanNo,
        string traceId,
        string baseUrl,
        CancellationToken ct);

    /// <summary>背景孔洞拍攝與辨識。</summary>
    /// <remarks>來自 Hank Scan3D 的 <c>Scan3D.Service.ScanOrchestrator</c>。</remarks>
    Task ExecuteHoleAsync(string taskId, CancellationToken ct);

    /// <summary>中止任務。</summary>
    /// <remarks>來自 Hank Scan3D 的 <c>Scan3D.Service.ScanOrchestrator</c>。</remarks>
    Task<ScanOutcome> AbortAsync(string taskId, string? kind, CancellationToken ct);

    /// <summary>查任務；不存在回 null。</summary>
    /// <remarks>來自 Hank Scan3D 的 <c>Scan3D.Service.TaskRepository</c>。</remarks>
    Task<TaskDocument?> FindAsync(string taskId, CancellationToken ct);

    /// <summary>下載產物。</summary>
    /// <remarks>來自 Hank Scan3D 的 <c>Scan3D.Service.ArtifactService</c>。</remarks>
    Task<(string Path, string ContentType, string DownloadName)> GetArtifactAsync(
        string taskId,
        string artifactId,
        CancellationToken ct);

    /// <summary>下載面掃描附件 ZIP。</summary>
    /// <remarks>來自 Hank Scan3D 的 <c>Scan3D.Service.ArtifactService</c>。</remarks>
    Task<(string Path, string ContentType, string DownloadName)> GetAttachmentsAsync(
        string taskId,
        CancellationToken ct);
}
