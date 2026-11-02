namespace ThreeDScan.Infrastructure.Export.Models;

/// <summary>端對端拼接參數。</summary>
/// <remarks>由 Hank Scan3D 的 ButtStitchOptions 搬入，邏輯不改。</remarks>
public sealed class ButtStitchOptions
{
    /// <summary>使用預設參數。</summary>
    public ButtStitchOptions()
    {
    }

    /// <summary>指定公稱間隙。</summary>
    /// <param name="nominalGapMm">公稱對接間隙（mm）。</param>
    public ButtStitchOptions(double nominalGapMm)
    {
        NominalGapMm = nominalGapMm;
    }

    /// <summary>公稱對接間隙（mm）。</summary>
    public double NominalGapMm { get; init; }

    /// <summary>平面擬合起點（mm）。</summary>
    public double PlaneTrainingStartMm { get; init; } = 25;

    /// <summary>平面擬合深度（mm）。</summary>
    public double PlaneTrainingDepthMm { get; init; } = 60;

    /// <summary>截面微調起點（mm）。</summary>
    public double ProfileTrainingStartMm { get; init; } = 12;

    /// <summary>截面微調終點（mm）。</summary>
    public double ProfileTrainingEndMm { get; init; } = 24;

    /// <summary>驗證深度（mm）。</summary>
    public double ValidationDepthMm { get; init; } = 10;

    /// <summary>截面分箱（mm）。</summary>
    public double ProfileBinMm { get; init; } = .5;

    /// <summary>截面邊緣留白（mm）。</summary>
    public double ProfileEdgeMarginMm { get; init; } = 5;

    /// <summary>稜線排除（mm）。</summary>
    public double ApexExclusionMm { get; init; } = 3;

    /// <summary>平面容差下限（mm）。</summary>
    public double MinimumPlaneToleranceMm { get; init; } = .1;

    /// <summary>端部帶寬（mm）。</summary>
    public double EndBandMm { get; init; } = 12;

    /// <summary>端部覆蓋分位。</summary>
    public double EndCoverageQuantile { get; init; } = .02;

    /// <summary>驗證 RMS 上限（mm）。</summary>
    public double MaximumValidationRmsMm { get; init; } = .5;

    /// <summary>驗證 P95 上限（mm）。</summary>
    public double MaximumValidationP95Mm { get; init; } = 1;

    /// <summary>全寬邊緣距離上限（mm）。</summary>
    public double MaximumSectionEdgeDistanceMm { get; init; } = 1.5;

    /// <summary>全寬覆蓋下限。</summary>
    public double MinimumSectionCoverageRatio { get; init; } = .98;
}
