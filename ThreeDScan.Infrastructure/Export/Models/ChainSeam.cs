using System.Text.Json.Serialization;

namespace ThreeDScan.Infrastructure.Export.Models;

/// <summary>多段鏈中的一條接縫。</summary>
/// <remarks>由 Hank Scan3D 的 ChainSeam 搬入，邏輯不改。</remarks>
public sealed class ChainSeam
{
    /// <summary>接縫編號（1 起）。</summary>
    public required int SeamNumber { get; init; }

    /// <summary>左段索引。</summary>
    public int LeftScanIndex
    {
        get
        {
            return SeamNumber - 1;
        }
    }

    /// <summary>右段索引。</summary>
    public int RightScanIndex
    {
        get
        {
            return SeamNumber;
        }
    }

    /// <summary>左端（局部對座標）。</summary>
    public required StitchedPart LeftEnd { get; init; }

    /// <summary>右端（局部對座標）。</summary>
    public required StitchedPart RightEnd { get; init; }

    /// <summary>局部→全域姿態。</summary>
    public required StitchPose LocalToGlobalPose { get; init; }

    /// <summary>驗證。</summary>
    public required StitchValidation Validation { get; init; }

    /// <summary>驗證截面（不序列化）。</summary>
    [JsonIgnore]
    public ProfileComparison[] ValidationProfile { get; init; } = [];

    /// <summary>全域接縫原點。</summary>
    public ScanVector WorldSeamOrigin
    {
        get
        {
            return LocalToGlobalPose.Apply(new ScanVector(0, Validation.SeamYmm, 0));
        }
    }

    /// <summary>全域接縫法線。</summary>
    public ScanVector WorldSeamNormal
    {
        get
        {
            return new ScanVector(LocalToGlobalPose.RowX.Y, LocalToGlobalPose.RowY.Y, LocalToGlobalPose.RowZ.Y);
        }
    }
}
