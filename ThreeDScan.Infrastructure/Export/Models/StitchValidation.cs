namespace ThreeDScan.Infrastructure.Export.Models;

/// <summary>接縫驗證。</summary>
/// <remarks>由 Hank Scan3D 的 StitchValidation 搬入，邏輯不改。</remarks>
public sealed class StitchValidation
{
    /// <summary>建立驗證。</summary>
    /// <param name="passed">是否通過。</param>
    /// <param name="profileSampleCount">截面樣本數。</param>
    /// <param name="crossSectionHeightRmsMm">截面高度 RMS（mm）。</param>
    /// <param name="crossSectionHeightMedianAbsoluteMm">截面高度絕對中位（mm）。</param>
    /// <param name="crossSectionHeightP95AbsoluteMm">截面高度 P95（mm）。</param>
    /// <param name="crossSectionHeightMaxAbsoluteMm">截面高度最大絕對（mm）。</param>
    /// <param name="profileSpanMm">截面跨度（mm）。</param>
    /// <param name="observedEnvelopeGapMm">觀測包絡間隙（mm）。</param>
    /// <param name="flangeAngleDifferenceDegrees">翼板角差（度）。</param>
    /// <param name="nominalGapMm">公稱間隙（mm）。</param>
    /// <param name="seamYmm">接縫 Y（mm）。</param>
    /// <param name="fitAndValidationIndicesDisjoint">擬合與驗證索引是否互斥。</param>
    /// <param name="method">方法說明。</param>
    /// <param name="absoluteLongitudinalPlacementVerified">是否驗證絕對長向位置。</param>
    /// <param name="physicalOverallLengthVerified">是否驗證物理總長。</param>
    /// <param name="fullSection">全寬檢查。</param>
    public StitchValidation(
        bool passed,
        int profileSampleCount,
        double crossSectionHeightRmsMm,
        double crossSectionHeightMedianAbsoluteMm,
        double crossSectionHeightP95AbsoluteMm,
        double crossSectionHeightMaxAbsoluteMm,
        double profileSpanMm,
        double observedEnvelopeGapMm,
        double flangeAngleDifferenceDegrees,
        double nominalGapMm,
        double seamYmm,
        bool fitAndValidationIndicesDisjoint,
        string method = "Held-out end cross-sections projected perpendicular to the long axis; no spatial overlap registration",
        bool absoluteLongitudinalPlacementVerified = false,
        bool physicalOverallLengthVerified = false,
        SectionEdgeCheck? fullSection = null)
    {
        Passed = passed;
        ProfileSampleCount = profileSampleCount;
        CrossSectionHeightRmsMm = crossSectionHeightRmsMm;
        CrossSectionHeightMedianAbsoluteMm = crossSectionHeightMedianAbsoluteMm;
        CrossSectionHeightP95AbsoluteMm = crossSectionHeightP95AbsoluteMm;
        CrossSectionHeightMaxAbsoluteMm = crossSectionHeightMaxAbsoluteMm;
        ProfileSpanMm = profileSpanMm;
        ObservedEnvelopeGapMm = observedEnvelopeGapMm;
        FlangeAngleDifferenceDegrees = flangeAngleDifferenceDegrees;
        NominalGapMm = nominalGapMm;
        SeamYmm = seamYmm;
        FitAndValidationIndicesDisjoint = fitAndValidationIndicesDisjoint;
        Method = method;
        AbsoluteLongitudinalPlacementVerified = absoluteLongitudinalPlacementVerified;
        PhysicalOverallLengthVerified = physicalOverallLengthVerified;
        FullSection = fullSection;
    }

    /// <summary>是否通過。</summary>
    public bool Passed { get; init; }

    /// <summary>截面樣本數。</summary>
    public int ProfileSampleCount { get; init; }

    /// <summary>截面高度 RMS（mm）。</summary>
    public double CrossSectionHeightRmsMm { get; init; }

    /// <summary>截面高度絕對中位（mm）。</summary>
    public double CrossSectionHeightMedianAbsoluteMm { get; init; }

    /// <summary>截面高度 P95（mm）。</summary>
    public double CrossSectionHeightP95AbsoluteMm { get; init; }

    /// <summary>截面高度最大絕對（mm）。</summary>
    public double CrossSectionHeightMaxAbsoluteMm { get; init; }

    /// <summary>截面跨度（mm）。</summary>
    public double ProfileSpanMm { get; init; }

    /// <summary>觀測包絡間隙（mm）。</summary>
    public double ObservedEnvelopeGapMm { get; init; }

    /// <summary>翼板角差（度）。</summary>
    public double FlangeAngleDifferenceDegrees { get; init; }

    /// <summary>公稱間隙（mm）。</summary>
    public double NominalGapMm { get; init; }

    /// <summary>接縫 Y（mm）。</summary>
    public double SeamYmm { get; init; }

    /// <summary>擬合與驗證索引是否互斥。</summary>
    public bool FitAndValidationIndicesDisjoint { get; init; }

    /// <summary>方法說明。</summary>
    public string Method { get; init; } = "Held-out end cross-sections projected perpendicular to the long axis; no spatial overlap registration";

    /// <summary>是否驗證絕對長向位置。</summary>
    public bool AbsoluteLongitudinalPlacementVerified { get; init; }

    /// <summary>是否驗證物理總長。</summary>
    public bool PhysicalOverallLengthVerified { get; init; }

    /// <summary>全寬檢查。</summary>
    public SectionEdgeCheck? FullSection { get; init; }

    /// <summary>未排除稀疏尾端分箱的原始截面邊界，供比對診斷；所有原始點仍留在量測輸出。</summary>
    public SectionEdgeCheck? UnfilteredFullSection { get; init; }

    /// <summary>邊界支承方法；RMS／P95 仍使用原始翼板留出截面。</summary>
    public string EdgeSamplingRule => "Measured transverse bins with >=3 points, longitudinal span >= half the band, and a three-column median of occupied longitudinal cells >= half the typical occupancy of their own wing; physical cell size is ProfileBinMm and measured neighbour gap is at most 1.5 mm. Independent end/interior bands use the same rule. Endpoints remain actual measured bin medians. Boundary filtering does not change the retained measurement output.";

    /// <summary>左端相對自身獨立內側訓練帶的橫向全翼寬檢查，投影至 U 軸、Z 設為零。</summary>
    public SectionEdgeCheck? LeftEndInteriorCoverage { get; init; }

    /// <summary>右端相對自身獨立內側訓練帶的橫向全翼寬檢查，投影至 U 軸、Z 設為零。</summary>
    public SectionEdgeCheck? RightEndInteriorCoverage { get; init; }

    /// <summary>內側翼寬檢查僅評估橫向投影；實際端對端高度另由 FullSection 與 RMS／P95 檢查。</summary>
    public string InteriorCoverageCoordinateFrame => "Transverse U projection only; Z=0 in own-interior width checks. FullSection compares actual measured end-to-end U/Z profiles.";

    /// <summary>左端驗證帶中符合訓練翼板平面的量測點比例；至少 70%。</summary>
    public double LeftEndWingSupportFraction { get; init; } = 1;

    /// <summary>右端驗證帶中符合訓練翼板平面的量測點比例；至少 70%。</summary>
    public double RightEndWingSupportFraction { get; init; } = 1;

    /// <summary>驗證點只依訓練平面分類，不能以端面雜點代替翼板外緣。</summary>
    public string WingSupportRule => "Corresponding left/right training wing selected by local transverse side: orthogonal distance <= max(MinimumPlaneToleranceMm, 3*wing RMS); no refitting on held-out points. At least 70% held-out support and each end's own interior-width check are required. Excluded profile noise remains in the retained measured point cloud.";
}
