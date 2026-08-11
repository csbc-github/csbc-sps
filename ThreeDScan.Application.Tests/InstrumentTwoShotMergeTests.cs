using Xunit;
using ThreeDScan.Application.Calibration;
using ThreeDScan.Application.Calibration.Dtos;

namespace ThreeDScan.Application.Tests;

/// <summary>UC-01 兩次拍攝合併邏輯（rmseXY／zDeviation）。</summary>
public sealed class InstrumentTwoShotMergeTests
{
    [Fact]
    public void Merge_AveragesZAndReportsZDeviation()
    {
        var seq2 = new InstrumentCalibrationComputeResult
        {
            ScannerToFloorZ = 502,
            ActualScannerPosition = new CoordinateDto { X = 1010, Y = 2010, Z = 502 },
            Rmse = 0.2,
            Compensation = new CompensationDto { OffsetX = 10, OffsetY = 10, OffsetZ = 2 },
        };

        var merged = InstrumentTwoShotMerge.Merge(
            seq1ScannerToFloorZ: 498,
            seq1ActualX: 1000,
            seq1ActualY: 2000,
            seq1RmseXy: 0.4,
            seq2: seq2,
            finalExpected: new CoordinateDto { X = 1000, Y = 2000, Z = 500 },
            zDeviationThresholdMm: 1.0);

        Assert.Equal(500, merged.ScannerToFloorZ, 6);
        Assert.Equal(4, merged.ZDeviation, 6);
        Assert.Equal(1005, merged.ActualScannerPosition.X, 6);
        Assert.Equal(2005, merged.ActualScannerPosition.Y, 6);
        Assert.Equal(0.4, merged.RmseXY, 6);
        Assert.Equal(5, merged.Compensation.OffsetX, 6);
        Assert.Equal(5, merged.Compensation.OffsetY, 6);
        Assert.Equal(0, merged.Compensation.OffsetZ, 6);
        Assert.False(merged.QualityOk); // zDeviation 4 >= 1
    }

    [Fact]
    public void Merge_QualityOk_WhenWithinThresholds()
    {
        var seq2 = new InstrumentCalibrationComputeResult
        {
            ScannerToFloorZ = 500.2,
            ActualScannerPosition = new CoordinateDto { X = 1000.1, Y = 2000.1, Z = 500.2 },
            Rmse = 0.3,
        };

        var merged = InstrumentTwoShotMerge.Merge(
            seq1ScannerToFloorZ: 499.8,
            seq1ActualX: 999.9,
            seq1ActualY: 1999.9,
            seq1RmseXy: 0.2,
            seq2: seq2,
            finalExpected: new CoordinateDto { X = 1000, Y = 2000, Z = 500 },
            zDeviationThresholdMm: 1.0);

        Assert.True(merged.QualityOk);
        Assert.Equal(0.4, merged.ZDeviation, 6);
        Assert.Equal(0.3, merged.RmseXY, 6);
    }

    [Fact]
    public void Merge_QualityFails_WhenRmseXyTooHigh()
    {
        var seq2 = new InstrumentCalibrationComputeResult
        {
            ScannerToFloorZ = 500,
            ActualScannerPosition = new CoordinateDto { X = 1000, Y = 2000, Z = 500 },
            Rmse = 1.5,
        };

        var merged = InstrumentTwoShotMerge.Merge(
            seq1ScannerToFloorZ: 500,
            seq1ActualX: 1000,
            seq1ActualY: 2000,
            seq1RmseXy: 0.1,
            seq2: seq2,
            finalExpected: new CoordinateDto { X = 1000, Y = 2000, Z = 500 },
            zDeviationThresholdMm: 1.0);

        Assert.False(merged.QualityOk);
        Assert.Equal(1.5, merged.RmseXY, 6);
    }
}
