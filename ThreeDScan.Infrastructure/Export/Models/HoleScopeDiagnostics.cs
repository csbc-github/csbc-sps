namespace ThreeDScan.Infrastructure.Export.Models;

/// <summary>Measured workpiece membership used to limit recorded openings to validated angle-steel wings.</summary>
public sealed record HoleScopeDiagnostics
{
    public string Mode { get; init; } = "AngleSteelOnly";
    public bool WorkpieceIdentified { get; init; }
    public string? Reason { get; init; }
    public int SourcePointCount { get; init; }
    public bool GroundReliable { get; init; }
    public int ValidatedWorkpieceCount { get; init; }
    public int AllowedSourcePointCount { get; init; }
    public int AllowedWingPlaneCount { get; init; }
    public double MinimumBoundaryMembershipFraction { get; init; } = .95;
    public double MaximumFaceNormalAngleDegrees { get; init; } = 5;
    public double MinimumFacePlaneToleranceMm { get; init; } = 1.5;
    public HoleScopeWorkpieceDiagnostic[] Workpieces { get; init; } = [];
    public int InputOpeningCount { get; init; }
    public int RetainedOpeningCount { get; init; }
    public string[] RetainedFaceIds { get; init; } = [];
    public string[] ExcludedOpeningIds { get; init; } = [];

    public HoleScopeDiagnostics WithOpenings(int inputOpeningCount, int retainedOpeningCount,
        string[] retainedFaceIds, string[] excludedOpeningIds) => this with
        {
            InputOpeningCount = inputOpeningCount,
            RetainedOpeningCount = retainedOpeningCount,
            RetainedFaceIds = retainedFaceIds.ToArray(),
            ExcludedOpeningIds = excludedOpeningIds.ToArray(),
        };
}

public sealed record HoleScopeWorkpieceDiagnostic
{
    public int WorkpieceNumber { get; init; }
    public int ParentComponentRank { get; init; }
    public bool SeparatedFromLocalSupport { get; init; }
    public int MeasuredPointCount { get; init; }
    public double WingNormalAngleDegrees { get; init; }
    public double LeftPlaneRmsMm { get; init; }
    public double RightPlaneRmsMm { get; init; }
}
