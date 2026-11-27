namespace ThreeDScan.Infrastructure.Export.Models;

/// <summary>單次實體重建摘要。</summary>
/// <remarks>由 Hank Scan3D 的 SolidReconstruction 搬入，邏輯不改。</remarks>
public sealed class SolidReconstruction
{
    /// <summary>建立摘要。</summary>
    /// <param name="origin">原點。</param>
    /// <param name="crossAxis">橫軸。</param>
    /// <param name="longAxis">長軸。</param>
    /// <param name="upAxis">上軸。</param>
    /// <param name="flangeSlope">翼板斜率。</param>
    /// <param name="faceNormalThicknessMm">法向板厚（mm）。</param>
    /// <param name="bisectorExtrusionMm">角平分線擠出（mm）。</param>
    /// <param name="leftPlaneRmsMm">左翼 RMS（mm）。</param>
    /// <param name="rightPlaneRmsMm">右翼 RMS（mm）。</param>
    /// <param name="filledSamplingCells">填補取樣格數。</param>
    /// <param name="removedAmbiguousCells">移除的歧義格數。</param>
    /// <param name="removedSmallIslandCells">移除的小島格數。</param>
    /// <param name="occupiedCells">佔用格數。</param>
    public SolidReconstruction(
        ScanVector origin,
        ScanVector crossAxis,
        ScanVector longAxis,
        ScanVector upAxis,
        double flangeSlope,
        double faceNormalThicknessMm,
        double bisectorExtrusionMm,
        double leftPlaneRmsMm,
        double rightPlaneRmsMm,
        int filledSamplingCells,
        int removedAmbiguousCells,
        int removedSmallIslandCells,
        int occupiedCells)
    {
        Origin = origin;
        CrossAxis = crossAxis;
        LongAxis = longAxis;
        UpAxis = upAxis;
        FlangeSlope = flangeSlope;
        FaceNormalThicknessMm = faceNormalThicknessMm;
        BisectorExtrusionMm = bisectorExtrusionMm;
        LeftPlaneRmsMm = leftPlaneRmsMm;
        RightPlaneRmsMm = rightPlaneRmsMm;
        FilledSamplingCells = filledSamplingCells;
        RemovedAmbiguousCells = removedAmbiguousCells;
        RemovedSmallIslandCells = removedSmallIslandCells;
        OccupiedCells = occupiedCells;
    }

    /// <summary>原點。</summary>
    public ScanVector Origin { get; init; }

    /// <summary>橫軸。</summary>
    public ScanVector CrossAxis { get; init; }

    /// <summary>長軸。</summary>
    public ScanVector LongAxis { get; init; }

    /// <summary>上軸。</summary>
    public ScanVector UpAxis { get; init; }

    /// <summary>翼板斜率。</summary>
    public double FlangeSlope { get; init; }

    /// <summary>法向板厚（mm）。</summary>
    public double FaceNormalThicknessMm { get; init; }

    /// <summary>角平分線擠出（mm）。</summary>
    public double BisectorExtrusionMm { get; init; }

    /// <summary>左翼 RMS（mm）。</summary>
    public double LeftPlaneRmsMm { get; init; }

    /// <summary>右翼 RMS（mm）。</summary>
    public double RightPlaneRmsMm { get; init; }

    /// <summary>填補取樣格數。</summary>
    public int FilledSamplingCells { get; init; }

    /// <summary>移除的歧義格數。</summary>
    public int RemovedAmbiguousCells { get; init; }

    /// <summary>移除的小島格數。</summary>
    public int RemovedSmallIslandCells { get; init; }

    /// <summary>佔用格數。</summary>
    public int OccupiedCells { get; init; }

    /// <summary>建模前的原始量測點數；點雲輸出仍完整保留這些量測。</summary>
    public int InputMeasurementCount { get; init; }

    /// <summary>符合獨立兩翼平面的實測點數。</summary>
    public int WingPlaneMeasurementCount { get; init; }

    /// <summary>平面之外、仍位於板厚／兩格稜線鄰域的實測圓角點數。</summary>
    public int MeasuredRidgePointCount { get; init; }

    /// <summary>模型外緣已依門檻及保留實體原框修補，新增材料為推估幾何。</summary>
    public bool OuterEdgesFilled { get; init; }

    /// <summary>未投影為實體足跡的端面／雜點數；不從量測點雲刪除。</summary>
    public int ExcludedFromFootprintMeasurementCount => Math.Max(0, InputMeasurementCount - WingPlaneMeasurementCount - MeasuredRidgePointCount);

    /// <summary>足跡來源限制，不包含補造的量測點或跨接空白。</summary>
    public string FootprintSelection => OuterEdgesFilled
        ? "The initial footprint uses independently supported measured wings and measured rounded-ridge samples. Outer-edge notches are repaired against each retained solid's original UV envelope using the configured physical-depth threshold; this border material is inferred, not measured. Original enclosed hole cells and separate major solids are preserved. Original measurements remain unchanged in PLY."
        : "Independently supported measured wing points plus actual rounded-ridge samples within max(material thickness, 2*mesh cell, 3*wing RMS) of the fitted ridge line. Other measurements remain in PLY but cannot create projected CAD islands. Empty seams/ridges and resolved holes are not bridged.";
}
