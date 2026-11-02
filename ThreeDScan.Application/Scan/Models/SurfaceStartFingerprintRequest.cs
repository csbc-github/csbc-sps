using ThreeDScan.Domain.Models;

namespace ThreeDScan.Application.Scan.Models;

/// <summary>面掃描 start 指紋來源（對齊 Hank SurfaceStartRequest 序列化形狀）。</summary>
public sealed class SurfaceStartFingerprintRequest
{
    /// <summary>掃描器編號。</summary>
    public string ScannerId { get; init; } = "";

    /// <summary>工單號。</summary>
    public string JobId { get; init; } = "";

    /// <summary>相機位姿。</summary>
    public Pose5 CameraPosition { get; init; } = new Pose5();

    /// <summary>Navigator Scan 編號。</summary>
    public int? NavigatorScanNo { get; init; }

    /// <summary>左內側點。</summary>
    public Point3? InnerLeft { get; init; }

    /// <summary>右內側點。</summary>
    public Point3? InnerRight { get; init; }

    /// <summary>材料厚度（mm）。</summary>
    public double? MaterialThicknessMm { get; init; }

    /// <summary>ROI。</summary>
    public ScannerRoi? Roi { get; init; }

    /// <summary>候選 rank。</summary>
    public int? ComponentRank { get; init; }

    /// <summary>拼接模式。</summary>
    public StitchMode StitchMode { get; init; }

    /// <summary>公稱間隙（mm）。</summary>
    public double NominalGapMm { get; init; }

    /// <summary>自 API start 請求建立指紋 DTO。</summary>
    /// <param name="request">start 請求。</param>
    /// <param name="cameraPosition">已轉換的位姿。</param>
    /// <param name="innerLeft">左內側。</param>
    /// <param name="innerRight">右內側。</param>
    /// <returns>指紋來源。</returns>
    public static SurfaceStartFingerprintRequest From(
        SurfaceScanStartRequest request,
        Pose5 cameraPosition,
        Point3? innerLeft,
        Point3? innerRight)
    {
        return new SurfaceStartFingerprintRequest
        {
            ScannerId = request.ScannerId,
            JobId = request.JobId,
            CameraPosition = cameraPosition,
            NavigatorScanNo = request.NavigatorScanNo,
            InnerLeft = innerLeft,
            InnerRight = innerRight,
            MaterialThicknessMm = request.MaterialThicknessMm,
            Roi = request.Roi,
            ComponentRank = request.ComponentRank,
            StitchMode = request.StitchMode,
            NominalGapMm = request.NominalGapMm,
        };
    }
}
