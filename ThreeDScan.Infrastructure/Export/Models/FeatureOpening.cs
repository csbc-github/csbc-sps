namespace ThreeDScan.Infrastructure.Export.Models;

/// <summary>單個開口／孔洞。</summary>
/// <remarks>由 Hank Scan3D 的 FeatureOpening 搬入，邏輯不改。</remarks>
public sealed class FeatureOpening
{
    /// <summary>建立開口。</summary>
    /// <param name="id">識別。</param>
    /// <param name="faceId">所屬面。</param>
    /// <param name="type">類型。</param>
    /// <param name="status">狀態。</param>
    /// <param name="closed">是否封閉。</param>
    /// <param name="center">中心。</param>
    /// <param name="boundary">邊界點。</param>
    /// <param name="shapeFitRmseMm">形狀擬合 RMS（mm）。</param>
    /// <param name="widthMm">寬（mm）。</param>
    /// <param name="heightMm">高（mm）。</param>
    /// <param name="scanlines">掃描列數。</param>
    /// <param name="reason">原因。</param>
    public FeatureOpening(
        string id,
        string faceId,
        string type,
        string status,
        bool closed,
        FeaturePoint center,
        FeaturePoint[] boundary,
        double? shapeFitRmseMm,
        double widthMm,
        double heightMm,
        int scanlines,
        string? reason)
    {
        Id = id;
        FaceId = faceId;
        Type = type;
        Status = status;
        Closed = closed;
        Center = center;
        Boundary = boundary;
        ShapeFitRmseMm = shapeFitRmseMm;
        WidthMm = widthMm;
        HeightMm = heightMm;
        Scanlines = scanlines;
        Reason = reason;
    }

    /// <summary>識別。</summary>
    public string Id { get; init; }

    /// <summary>所屬面。</summary>
    public string FaceId { get; init; }

    /// <summary>類型。</summary>
    public string Type { get; init; }

    /// <summary>狀態。</summary>
    public string Status { get; init; }

    /// <summary>是否封閉。</summary>
    public bool Closed { get; init; }

    /// <summary>中心。</summary>
    public FeaturePoint Center { get; init; }

    /// <summary>邊界點。</summary>
    public FeaturePoint[] Boundary { get; init; }

    /// <summary>形狀擬合 RMS（mm）。</summary>
    public double? ShapeFitRmseMm { get; init; }

    /// <summary>寬（mm）。</summary>
    public double WidthMm { get; init; }

    /// <summary>高（mm）。</summary>
    public double HeightMm { get; init; }

    /// <summary>掃描列數。</summary>
    public int Scanlines { get; init; }

    /// <summary>原因。</summary>
    public string? Reason { get; init; }

    /// <summary>原始 scanner XYZ、面內 U/V 及來源 PLY 頂點索引；不使用擬合點取代實測孔緣。</summary>
    public BoundaryPoint[] MeasuredBoundary { get; init; } = [];

    /// <summary>每條掃描列的左右實測孔緣端點，來源索引指向輸入 PLY。</summary>
    public HoleEdgePair[] EdgePairs { get; init; } = [];

    public BoundaryBox? BoundaryBox { get; init; }
    public HoleClosure? Closure { get; init; }
    public OpeningWidthRange? OpeningWidthRangeMm { get; init; }
    public string BoundarySource { get; init; } = "Measured scan-row gap endpoints and measured closing-rim points";
}
