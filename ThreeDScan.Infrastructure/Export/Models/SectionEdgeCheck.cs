namespace ThreeDScan.Infrastructure.Export.Models;

/// <summary>全寬邊緣檢查。</summary>
/// <remarks>由 Hank Scan3D 的 SectionEdgeCheck 搬入，邏輯不改。</remarks>
public sealed class SectionEdgeCheck
{
    /// <summary>建立檢查結果。</summary>
    /// <param name="passed">是否通過。</param>
    /// <param name="leftEdgeDistanceMm">左緣距離（mm）。</param>
    /// <param name="rightEdgeDistanceMm">右緣距離（mm）。</param>
    /// <param name="commonCoverageRatio">共同覆蓋比。</param>
    /// <param name="leftWidthMm">左段寬（mm）。</param>
    /// <param name="rightWidthMm">右段寬（mm）。</param>
    /// <param name="leftScanLeftEdge">左段左緣。</param>
    /// <param name="rightScanLeftEdge">右段左緣。</param>
    /// <param name="leftScanRightEdge">左段右緣。</param>
    /// <param name="rightScanRightEdge">右段右緣。</param>
    public SectionEdgeCheck(
        bool passed,
        double leftEdgeDistanceMm,
        double rightEdgeDistanceMm,
        double commonCoverageRatio,
        double leftWidthMm,
        double rightWidthMm,
        ProfilePoint leftScanLeftEdge,
        ProfilePoint rightScanLeftEdge,
        ProfilePoint leftScanRightEdge,
        ProfilePoint rightScanRightEdge)
    {
        Passed = passed;
        LeftEdgeDistanceMm = leftEdgeDistanceMm;
        RightEdgeDistanceMm = rightEdgeDistanceMm;
        CommonCoverageRatio = commonCoverageRatio;
        LeftWidthMm = leftWidthMm;
        RightWidthMm = rightWidthMm;
        LeftScanLeftEdge = leftScanLeftEdge;
        RightScanLeftEdge = rightScanLeftEdge;
        LeftScanRightEdge = leftScanRightEdge;
        RightScanRightEdge = rightScanRightEdge;
    }

    /// <summary>是否通過。</summary>
    public bool Passed { get; init; }

    /// <summary>左緣距離（mm）。</summary>
    public double LeftEdgeDistanceMm { get; init; }

    /// <summary>右緣距離（mm）。</summary>
    public double RightEdgeDistanceMm { get; init; }

    /// <summary>共同覆蓋比。</summary>
    public double CommonCoverageRatio { get; init; }

    /// <summary>左段寬（mm）。</summary>
    public double LeftWidthMm { get; init; }

    /// <summary>右段寬（mm）。</summary>
    public double RightWidthMm { get; init; }

    /// <summary>左段左緣。</summary>
    public ProfilePoint LeftScanLeftEdge { get; init; }

    /// <summary>右段左緣。</summary>
    public ProfilePoint RightScanLeftEdge { get; init; }

    /// <summary>左段右緣。</summary>
    public ProfilePoint LeftScanRightEdge { get; init; }

    /// <summary>右段右緣。</summary>
    public ProfilePoint RightScanRightEdge { get; init; }
}
