namespace ThreeDScan.Infrastructure.Export.Models;

/// <summary>兩段端對端拼接結果。</summary>
/// <remarks>由 Hank Scan3D 的 ButtStitchResult 搬入，邏輯不改。</remarks>
public sealed class ButtStitchResult
{
    /// <summary>A 段。</summary>
    public required StitchedPart PartA { get; init; }

    /// <summary>B 段。</summary>
    public required StitchedPart PartB { get; init; }

    /// <summary>參數。</summary>
    public required ButtStitchOptions Options { get; init; }

    /// <summary>驗證。</summary>
    public required StitchValidation Validation { get; init; }

    /// <summary>驗證截面比較。</summary>
    public required ProfileComparison[] ValidationProfile { get; init; }

    /// <summary>包圍盒。</summary>
    public required ScanBounds Bounds { get; init; }

    /// <summary>2D 截面修正。</summary>
    public required double[][] ProfileCorrection2D { get; init; }

    /// <summary>來源總點數。</summary>
    public int TotalSourcePoints
    {
        get
        {
            return PartA.SourcePointCount + PartB.SourcePointCount;
        }
    }

    /// <summary>拼接後點數。</summary>
    public int StitchedPointCount
    {
        get
        {
            return PartA.RetainedPointCount + PartB.RetainedPointCount;
        }
    }

    /// <summary>假設說明。</summary>
    public string[] Assumptions { get; init; } =
    [
        "The caller selected a nominal end-to-end assembly of the two angle-steel scans; physical adjacency and capture overlap were not independently verified.",
        "Find each workpiece's longitudinal direction from its independently supported XYZ wing planes, oriented toward scanner +Y (+X when Y is zero); join A's upper fitted-axis end to B's lower fitted-axis end at the configured nominal gap. End-frame Y is not scanner Y.",
        "Trim oblique scan boundaries to transverse join planes. Original input PLY files retain every measurement; optional diagnostic exports retain the complete transformed scans. Do not fabricate or fill points.",
        "Classify end-section measurements against independently trained wing planes; off-wing noise stays in the measured output but cannot define a wing edge. Each end must also cover its own interior measured wing width.",
        "Only rigid transforms are applied. Held-out cross-section agreement does not verify physical total length or the unknown motion between captures.",
    ];
}
