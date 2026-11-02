namespace ThreeDScan.Infrastructure.Export.Models;

/// <summary>偵測到的開口。</summary>
/// <remarks>由 Hank Scan3D 的 DetectedHole 搬入，邏輯不改。</remarks>
public sealed class DetectedHole
{
    /// <summary>所屬面。</summary>
    public string FaceId { get; init; } = "";

    /// <summary>狀態。</summary>
    public string Status { get; init; } = "";

    /// <summary>原因。</summary>
    public string? Reason { get; init; }

    /// <summary>面內 U。</summary>
    public double U { get; init; }

    /// <summary>面內 V。</summary>
    public double V { get; init; }

    /// <summary>X。</summary>
    public double X { get; init; }

    /// <summary>Y。</summary>
    public double Y { get; init; }

    /// <summary>Z。</summary>
    public double Z { get; init; }

    /// <summary>掃描列數。</summary>
    public int Scanlines { get; init; }

    /// <summary>邊緣點數。</summary>
    public int EdgePointCount { get; init; }

    /// <summary>周界點。</summary>
    public BoundaryPoint[] PerimeterPoints { get; init; } = [];

    /// <summary>孔內點。</summary>
    public BoundaryPoint[] HolePoints { get; init; } = [];

    /// <summary>面內包圍盒。</summary>
    public BoundaryBox BoundaryBox { get; init; } = new BoundaryBox(0, 0, 0, 0, 0, 0);

    /// <summary>開口寬度範圍。</summary>
    public OpeningWidthRange OpeningWidthRangeMm { get; init; } = new OpeningWidthRange(0, 0, 0);

    /// <summary>封口狀態。</summary>
    public HoleClosure Closure { get; init; } = new HoleClosure(false, false);

    /// <summary>邊緣對。</summary>
    public HoleEdgePair[] EdgePoints { get; init; } = [];

    /// <summary>邊界來源說明。</summary>
    public string BoundarySource { get; init; } = "Measured scan-row gap endpoints and measured closing-rim points";

    /// <summary>識別。</summary>
    public string Id { get; set; } = "";
}
