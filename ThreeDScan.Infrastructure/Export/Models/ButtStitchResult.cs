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
        "The inputs scan two contiguous parts of the same workpiece with no spatial overlap, as confirmed by the user.",
        "Join A maximum scanner-Y end to B minimum scanner-Y end; the unknown physical longitudinal gap is assumed to be the configured nominal gap.",
        "Trim oblique scan boundaries to transverse join planes. Keep the complete transformed scans in a separate file; do not fabricate or fill points.",
        "Only rigid transforms are applied. Held-out cross-section agreement does not verify physical total length or the unknown motion between captures.",
    ];
}
