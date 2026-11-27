using ThreeDScan.Domain.Models;

namespace ThreeDScan.Infrastructure.Export.Models;

/// <summary>Local raised support-plane separation evidence; never a scanner/world transform.</summary>
public sealed class LocalSupportDiagnostics
{
    public bool Detected { get; init; }
    public string? Reason { get; init; }
    public int ParentPointCount { get; init; }
    public int SupportPointCount { get; init; }
    public double SupportFraction { get; init; }
    public Point3? Normal { get; init; }
    public double PlaneD { get; init; }
    public double RmsMm { get; init; }
    public double AngleToFloorDegrees { get; init; }
    public double BelowSupportFraction { get; init; }
    public double SeedThresholdMm { get; init; }
    public string SeedHeightConvention { get; init; } = "Vertical scanner Z above the local support plane; segmentation only.";
    public int HighSeedCount { get; init; }
    /// <summary>Normal distance above the measured support required for residual connectivity.</summary>
    public double ResidualClearanceMm { get; init; }
    public int ResidualPointCount { get; init; }
    public int SeedComponentCount { get; init; }
    public int ValidChildCount { get; init; }
    public LocalSupportChildDiagnostic[] Children { get; init; } = [];
}

/// <summary>Compact provenance counts and validation result for one high-seed child.</summary>
public sealed class LocalSupportChildDiagnostic
{
    public int ChildRank { get; init; }
    public int SeedPointCount { get; init; }
    public int RecoveredPointCount { get; init; }
    public int CellCount { get; init; }
    public int FinalPointCount { get; init; }
    public bool FitAccepted { get; init; }
    public string? Reason { get; init; }
}
