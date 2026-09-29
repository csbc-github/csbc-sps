using ThreeDScan.Application.Calibration.Models;
using ThreeDScan.Application.Common.Models;
using ThreeDScan.Domain.Models;

namespace ThreeDScan.Application.Calibration;

/// <summary>UC-01 兩次拍攝結果合併（Z 平均、rmseXY／zDeviation、寫入判定）。</summary>
public static class InstrumentTwoShotMerge
{
    /// <summary>
    /// 合併兩幀單次計算結果；offset 相對第二次的 <paramref name="finalExpected"/>。
    /// </summary>
    public static InstrumentTwoShotMergeResult Merge(
        double seq1ScannerToFloorZ,
        double seq1ActualX,
        double seq1ActualY,
        double seq1RmseXy,
        InstrumentCalibrationComputeResult seq2,
        CoordinateDto finalExpected,
        double zDeviationThresholdMm,
        double rmseXyThresholdMm = InstrumentCalibrationService.RmseThresholdMm)
    {
        var scannerToFloorZ = (seq1ScannerToFloorZ + seq2.ScannerToFloorZ) / 2.0;
        var zDeviation = Math.Abs(seq1ScannerToFloorZ - seq2.ScannerToFloorZ);
        var actualX = (seq1ActualX + seq2.ActualScannerPosition.X) / 2.0;
        var actualY = (seq1ActualY + seq2.ActualScannerPosition.Y) / 2.0;
        var rmseXY = Math.Max(seq1RmseXy, seq2.Rmse);

        var compensation = new CompensationDto
        {
            OffsetX = actualX - finalExpected.X,
            OffsetY = actualY - finalExpected.Y,
            OffsetZ = scannerToFloorZ - finalExpected.Z,
        };

        var qualityOk =
            rmseXY < rmseXyThresholdMm &&
            zDeviation < zDeviationThresholdMm;

        return new InstrumentTwoShotMergeResult
        {
            Compensation = compensation,
            ActualScannerPosition = new CoordinateDto
            {
                X = actualX,
                Y = actualY,
                Z = scannerToFloorZ,
            },
            ScannerToFloorZ = scannerToFloorZ,
            ZDeviation = zDeviation,
            RmseXY = rmseXY,
            QualityOk = qualityOk,
        };
    }
}
