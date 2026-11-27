using ThreeDScan.Domain.Models;

namespace ThreeDScan.Application.Scan;

/// <summary>依使用者選定的翼緣驗證政策，決定是否可交付已通過對齊檢查的點雲。</summary>
public static class StitchCompletionPolicy
{
    /// <summary>嚴格品質已通過，或明確允許僅翼緣檢查失敗時交付點雲。</summary>
    /// <param name="result">保留原有嚴格品質旗標及失敗明細的拼接結果。</param>
    /// <param name="requireVerifiedBoundaries">是否要求翼緣檢查也須通過。</param>
    public static bool CanCompletePointCloud(StitchProcessingResult result, bool requireVerifiedBoundaries)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.ValidationPassed) return true;
        if (requireVerifiedBoundaries || result.AlignmentValidationPassed is not true || result.BoundaryValidationPassed is not false)
            return false;
        return result.ValidationFailures is { Length: > 0 } failures
            && failures.All(failure => failure is not null && IsBoundaryCheck(failure.Check));
    }

    private static bool IsBoundaryCheck(string check) => check is
        "fullSection.leftEdgeDistanceMm" or
        "fullSection.rightEdgeDistanceMm" or
        "fullSection.commonCoverageRatio" or
        "leftEndInteriorCoverage.leftEdgeDistanceMm" or
        "leftEndInteriorCoverage.rightEdgeDistanceMm" or
        "leftEndInteriorCoverage.commonCoverageRatio" or
        "rightEndInteriorCoverage.leftEdgeDistanceMm" or
        "rightEndInteriorCoverage.rightEdgeDistanceMm" or
        "rightEndInteriorCoverage.commonCoverageRatio";
}
