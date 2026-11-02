using ThreeDScan.Application.Calibration.Models;
using ThreeDScan.Application.Common.Models;
using ThreeDScan.Domain.Models;

namespace ThreeDScan.Application.Calibration;

/// <summary>UC-01 兩次拍攝結果合併（補償平均、Hank 品質檢查、寫入判定）。</summary>
/// <remarks>由 Hank Scan3D 的 SphereCalibration.Complete 搬入品質條件，邏輯不改。</remarks>
public static class InstrumentTwoShotMerge
{
    /// <summary>
    /// 合併兩次補償：直接平均；四項品質都要小於門檻才可寫入。
    /// </summary>
    /// <remarks>來自 Hank Scan3D 的 <c>Scan3D.Features.SphereCalibration</c>。</remarks>
    /// <param name="seq1Compensation">第一次拍攝的補償。</param>
    /// <param name="seq1RmseXy">第一次球距 RMSE。</param>
    /// <param name="seq1PositionResidualMm">第一次各球位置殘差。</param>
    /// <param name="seq2">第二次計算結果。</param>
    /// <param name="finalExpected">第二次攝影機預期位置。</param>
    /// <param name="qualityThresholdMm">品質門檻，預設 1 mm。</param>
    /// <returns>平均補償與品質判定。</returns>
    public static InstrumentTwoShotMergeResult Merge(
        CompensationDto seq1Compensation,
        double seq1RmseXy,
        double seq1PositionResidualMm,
        InstrumentCalibrationComputeResult seq2,
        CoordinateDto finalExpected,
        double qualityThresholdMm = InstrumentCalibrationService.RmseThresholdMm)
    {
        var offsetX = (seq1Compensation.OffsetX + seq2.Compensation.OffsetX) / 2.0;
        var offsetY = (seq1Compensation.OffsetY + seq2.Compensation.OffsetY) / 2.0;
        var offsetZ = (seq1Compensation.OffsetZ + seq2.Compensation.OffsetZ) / 2.0;
        var zDeviation = Math.Abs(seq1Compensation.OffsetZ - seq2.Compensation.OffsetZ);
        var rmseXY = Math.Sqrt(((seq1RmseXy * seq1RmseXy) + (seq2.Rmse * seq2.Rmse)) / 2.0);
        var positionResidual = Math.Max(seq1PositionResidualMm, seq2.PositionResidualMm);
        var dx = seq1Compensation.OffsetX - seq2.Compensation.OffsetX;
        var dy = seq1Compensation.OffsetY - seq2.Compensation.OffsetY;
        var xyOffsetDeviation = Math.Sqrt((dx * dx) + (dy * dy));
        var qualityOk =
            rmseXY < qualityThresholdMm
            && zDeviation < qualityThresholdMm
            && positionResidual < qualityThresholdMm
            && xyOffsetDeviation < qualityThresholdMm;

        var compensation = new CompensationDto
        {
            OffsetX = offsetX,
            OffsetY = offsetY,
            OffsetZ = offsetZ,
        };

        return new InstrumentTwoShotMergeResult
        {
            Compensation = compensation,
            ActualScannerPosition = new CoordinateDto
            {
                X = finalExpected.X + offsetX,
                Y = finalExpected.Y + offsetY,
                Z = finalExpected.Z + offsetZ,
            },
            ScannerToFloorZ = finalExpected.Z + offsetZ,
            ZDeviation = zDeviation,
            RmseXY = rmseXY,
            PositionResidualMm = positionResidual,
            XyOffsetDeviationMm = xyOffsetDeviation,
            QualityOk = qualityOk,
        };
    }
}
