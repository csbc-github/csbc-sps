using System.Text.Json.Serialization;

namespace ThreeDScan.Infrastructure.Export.Models;

/// <summary>單段拼接結果。</summary>
/// <remarks>由 Hank Scan3D 的 StitchedPart 搬入，邏輯不改。</remarks>
public sealed class StitchedPart
{
    /// <summary>來源點雲（不序列化）。</summary>
    [JsonIgnore]
    public ScanCloud Source { get; init; } = new ScanCloud();

    /// <summary>轉換後點（不序列化）。</summary>
    [JsonIgnore]
    public ScanVector[] TransformedPoints { get; init; } = [];

    /// <summary>是否保留（不序列化）。</summary>
    [JsonIgnore]
    public bool[] Retained { get; init; } = [];

    /// <summary>端部框。</summary>
    public required EndFrame Frame { get; init; }

    /// <summary>姿態。</summary>
    public required StitchPose Pose { get; init; }

    /// <summary>裁切座標（mm）。</summary>
    public required double CutCoordinateMm { get; init; }

    /// <summary>平面擬合索引。</summary>
    public required int[] PlaneTrainingIndices { get; init; }

    /// <summary>截面微調索引。</summary>
    public required int[] ProfileTrainingIndices { get; init; }

    /// <summary>驗證索引。</summary>
    public required int[] ValidationIndices { get; init; }

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
            return Retained.Count(k => k);
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
