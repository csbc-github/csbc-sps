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
}
