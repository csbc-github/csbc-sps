namespace ThreeDScan.Infrastructure.Export.Models;

/// <summary>單面檢視（含取樣與開口）。</summary>
/// <remarks>由 Hank Scan3D 的 HoleFaceView 搬入，邏輯不改。</remarks>
public sealed class HoleFaceView
{
    /// <summary>建立檢視。</summary>
    /// <param name="faceId">面識別。</param>
    /// <param name="plane">平面。</param>
    /// <param name="samples">取樣。</param>
    /// <param name="minU">U 最小。</param>
    /// <param name="maxU">U 最大。</param>
    /// <param name="minV">V 最小。</param>
    /// <param name="maxV">V 最大。</param>
    /// <param name="holes">封閉開口。</param>
    /// <param name="partialHoles">部分開口。</param>
    public HoleFaceView(
        string faceId,
        HoleFace plane,
        FaceSample[] samples,
        double minU,
        double maxU,
        double minV,
        double maxV,
        DetectedHole[] holes,
        DetectedHole[] partialHoles)
    {
        FaceId = faceId;
        Plane = plane;
        Samples = samples;
        MinU = minU;
        MaxU = maxU;
        MinV = minV;
        MaxV = maxV;
        Holes = holes;
        PartialHoles = partialHoles;
    }

    /// <summary>面識別。</summary>
    public string FaceId { get; init; }

    /// <summary>平面。</summary>
    public HoleFace Plane { get; init; }

    /// <summary>取樣。</summary>
    public FaceSample[] Samples { get; init; }

    /// <summary>U 最小。</summary>
    public double MinU { get; init; }

    /// <summary>U 最大。</summary>
    public double MaxU { get; init; }

    /// <summary>V 最小。</summary>
    public double MinV { get; init; }

    /// <summary>V 最大。</summary>
    public double MaxV { get; init; }

    /// <summary>封閉開口。</summary>
    public DetectedHole[] Holes { get; init; }

    /// <summary>部分開口。</summary>
    public DetectedHole[] PartialHoles { get; init; }
}
