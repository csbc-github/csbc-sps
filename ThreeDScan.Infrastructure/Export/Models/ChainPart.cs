using System.Text.Json.Serialization;

namespace ThreeDScan.Infrastructure.Export.Models;

/// <summary>多段鏈中的一段。</summary>
/// <remarks>由 Hank Scan3D 的 ChainPart 搬入，邏輯不改。</remarks>
public sealed class ChainPart
{
    /// <summary>段索引。</summary>
    public required int ScanIndex { get; init; }

    /// <summary>來源點雲（不序列化）。</summary>
    [JsonIgnore]
    public ScanCloud Source { get; init; } = new ScanCloud();

    /// <summary>轉換後點（不序列化）。</summary>
    [JsonIgnore]
    public ScanVector[] TransformedPoints { get; init; } = [];

    /// <summary>是否保留（不序列化）。</summary>
    [JsonIgnore]
    public bool[] Retained { get; init; } = [];

    /// <summary>姿態。</summary>
    public required StitchPose Pose { get; init; }

    /// <summary>來源點數。</summary>
    public int SourcePointCount
    {
        get
        {
            return Source.Points.Length;
        }
    }

    /// <summary>保留點數。</summary>
    public int RetainedPointCount
    {
        get
        {
            return Retained.Count(value => value);
        }
    }

    /// <summary>移除點數。</summary>
    public int RemovedPointCount
    {
        get
        {
            return SourcePointCount - RetainedPointCount;
        }
    }
}
