namespace ThreeDScan.Infrastructure.Export.Models;

/// <summary>僅供物件分離的地面平面支持證據；不代表掃描器外參校正。</summary>
public sealed class GroundPlaneDiagnostics
{
    public int SampleCount { get; init; }
    public int OccupiedRegionCount { get; init; }
    public int SupportingRegionCount { get; init; }
    public int PeripheralRegionCount { get; init; }
    public int SupportingPeripheralRegionCount { get; init; }
    public int SupportPointCount { get; init; }
    public double SupportFraction { get; init; }
    public double BelowPlaneFraction { get; init; }
    public double CoverageXFraction { get; init; }
    public double CoverageYFraction { get; init; }
    public double PeripheralSupportFraction { get; init; }
    public int HypothesisCount { get; init; }
    public bool Reliable { get; init; }
    public string? Warning { get; init; }
}
