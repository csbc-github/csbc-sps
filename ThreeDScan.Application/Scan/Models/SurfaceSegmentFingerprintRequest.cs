using ThreeDScan.Domain.Models;

namespace ThreeDScan.Application.Scan.Models;

/// <summary>面掃描 segment 指紋來源（對齊 Hank SurfaceSegmentRequest 屬性順序）。</summary>
public sealed class SurfaceSegmentFingerprintRequest
{
    /// <summary>任務識別碼。</summary>
    public string TaskId { get; init; } = "";

    /// <summary>相機位姿。</summary>
    public Pose5 CameraPosition { get; init; } = new Pose5();

    /// <summary>Navigator Scan 編號。</summary>
    public int? NavigatorScanNo { get; init; }

    /// <summary>段鍵（含 Idempotency-Key 後備）。</summary>
    public string? SegmentKey { get; init; }

    /// <summary>ROI。</summary>
    public ScannerRoi? Roi { get; init; }

    /// <summary>候選 rank。</summary>
    public int? ComponentRank { get; init; }

    /// <summary>自 segment 請求建立指紋 DTO。</summary>
    /// <param name="request">segment 請求。</param>
    /// <param name="cameraPosition">已轉換位姿。</param>
    /// <returns>指紋來源。</returns>
    public static SurfaceSegmentFingerprintRequest From(
        SurfaceScanSegmentRequest request,
        Pose5 cameraPosition)
    {
        return new SurfaceSegmentFingerprintRequest
        {
            TaskId = request.TaskId,
            CameraPosition = cameraPosition,
            NavigatorScanNo = request.NavigatorScanNo,
            SegmentKey = request.SegmentKey,
            Roi = request.Roi,
            ComponentRank = request.ComponentRank,
        };
    }
}
