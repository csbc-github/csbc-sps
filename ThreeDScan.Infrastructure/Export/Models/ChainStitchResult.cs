using System.Text.Json.Serialization;

namespace ThreeDScan.Infrastructure.Export.Models;

/// <summary>多段鏈拼接結果。</summary>
/// <remarks>由 Hank Scan3D 的 ChainStitchResult 搬入，邏輯不改。</remarks>
public sealed class ChainStitchResult
{
    /// <summary>各段。</summary>
    public required ChainPart[] Parts { get; init; }

    /// <summary>接縫。</summary>
    public required ChainSeam[] Seams { get; init; }

    /// <summary>參數。</summary>
    public required ButtStitchOptions Options { get; init; }

    /// <summary>包圍盒。</summary>
    public required ScanBounds Bounds { get; init; }

    /// <summary>兩段時的原版配對（不序列化）。</summary>
    [JsonIgnore]
    public ButtStitchResult? LegacyPair { get; init; }

    /// <summary>段數。</summary>
    public int ScanCount
    {
        get
        {
            return Parts.Length;
        }
    }

    /// <summary>接縫數。</summary>
    public int SeamCount
    {
        get
        {
            return Seams.Length;
        }
    }

    /// <summary>是否全部通過。</summary>
    public bool Passed
    {
        get
        {
            return Seams.All(seam => seam.Validation.Passed);
        }
    }

    /// <summary>來源總點數。</summary>
    public int TotalSourcePoints
    {
        get
        {
            return Parts.Sum(part => part.SourcePointCount);
        }
    }

    /// <summary>拼接後點數。</summary>
    public int StitchedPointCount
    {
        get
        {
            return Parts.Sum(part => part.RetainedPointCount);
        }
    }

    /// <summary>是否驗證物理總長。</summary>
    public bool PhysicalOverallLengthVerified
    {
        get
        {
            return false;
        }
    }

    /// <summary>假設說明。</summary>
    public string[] Assumptions { get; init; } =
    [
        "The ordered inputs are contiguous non-overlapping captures of the same angle steel, in millimetres, with organized scanner-Y rows.",
        "Join each input's maximum scanner-Y end to the next input's minimum scanner-Y end; every physical gap is assumed to equal the configured nominal gap.",
        "Solve adjacent original captures, compose rigid poses, intersect both end masks for interior segments, and transform each original point once without intermediate PLY quantization.",
        "Every seam is validated using final retained held-out points. Cross-section agreement does not verify absolute longitudinal placement or physical overall length.",
    ];
}
