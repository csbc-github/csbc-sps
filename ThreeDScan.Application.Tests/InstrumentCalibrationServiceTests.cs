using Xunit;
using ThreeDScan.Application.Calibration;
using ThreeDScan.Application.Calibration.Dtos;
using ThreeDScan.Domain.Models;

namespace ThreeDScan.Application.Tests;

/// <summary>InstrumentCalibrationService 單元測試。</summary>
public sealed class InstrumentCalibrationServiceTests
{
    private readonly InstrumentCalibrationService _sut = new();

    /// <summary>單球：XY offset 由球反推；Z offset = scannerToFloorZ − expected.z。</summary>
    [Fact]
    public void SingleBall_ComputesOffsetAsKnownMinusMeasuredMinusExpected()
    {
        var result = _sut.Compute(
            new CoordinateDto { X = 100, Y = 200, Z = 50 },
            [new StandardBallDto { BallId = "B1", KnownX = 160, KnownY = 390, KnownZ = 12 }],
            [new MeasuredBall { Index = 0, X = 10, Y = 20, Z = 2, Diameter = 24.9, IsWithinTolerance = true }]);

        Assert.Equal(50, result.Compensation.OffsetX, 3);
        Assert.Equal(170, result.Compensation.OffsetY, 3);
        Assert.Equal(-10, result.Compensation.OffsetZ, 3);
        Assert.Equal(40, result.ScannerToFloorZ, 3);
    }

    /// <summary>三球共線且預期位置為原點時，補償應趨近零。</summary>
    [Fact]
    public void ThreeBalls_IdentityLayout_ZeroOffsetWhenExpectedMatchesActual()
    {
        var expected = new CoordinateDto { X = 0, Y = 0, Z = 0 };
        var standards = new[]
        {
            new StandardBallDto { BallId = "B1", KnownX = 10, KnownY = 0, KnownZ = 0 },
            new StandardBallDto { BallId = "B2", KnownX = 20, KnownY = 0, KnownZ = 0 },
            new StandardBallDto { BallId = "B3", KnownX = 30, KnownY = 0, KnownZ = 0 },
        };
        var measured = new[]
        {
            new MeasuredBall { Index = 0, X = 10, Y = 0, Z = 0, Diameter = 24.9, IsWithinTolerance = true },
            new MeasuredBall { Index = 1, X = 20, Y = 0, Z = 0, Diameter = 24.9, IsWithinTolerance = true },
            new MeasuredBall { Index = 2, X = 30, Y = 0, Z = 0, Diameter = 24.9, IsWithinTolerance = true },
        };

        var result = _sut.Compute(expected, standards, measured);

        Assert.True(Math.Abs(result.Compensation.OffsetX) < 1e-6);
        Assert.True(Math.Abs(result.Compensation.OffsetY) < 1e-6);
        Assert.True(Math.Abs(result.Compensation.OffsetZ) < 1e-6);
    }

    /// <summary>量測球數少於台船傳入數時，應拋出 CAL_FEED_001。</summary>
    [Fact]
    public void InsufficientMeasuredBalls_ThrowsCalFeed001()
    {
        var ex = Assert.Throws<InstrumentCalibrationException>(() =>
            _sut.Compute(
                new CoordinateDto(),
                [
                    new StandardBallDto { BallId = "B1", KnownX = 1, KnownY = 2, KnownZ = 3 },
                    new StandardBallDto { BallId = "B2", KnownX = 4, KnownY = 5, KnownZ = 6 },
                ],
                [new MeasuredBall { Index = 0, X = 1, Y = 2, Z = 3, Diameter = 24.9, IsWithinTolerance = true }]));

        Assert.Equal("CAL_FEED_001", ex.ErrorCode);
    }

    /// <summary>掃描器僅 X 方向偏移 5 mm 時，補償 X 應為 5。</summary>
    [Fact]
    public void ScannerShift_ProducesNonZeroOffset()
    {
        var result = _sut.Compute(
            new CoordinateDto { X = 100, Y = 100, Z = 100 },
            [new StandardBallDto { BallId = "B1", KnownX = 110, KnownY = 100, KnownZ = 100 }],
            [new MeasuredBall { Index = 0, X = 5, Y = 0, Z = 100, Diameter = 24.9, IsWithinTolerance = true }]);

        Assert.Equal(5, result.Compensation.OffsetX, 2);
        Assert.Equal(0, result.Compensation.OffsetY, 2);
        Assert.Equal(0, result.Compensation.OffsetZ, 2);
    }

    /// <summary>滾輪模型：scannerToFloorZ = c.z − (b.z − a.z)。</summary>
    [Fact]
    public void SingleBall_ComputesScannerToFloorZ_FromRollerModel()
    {
        var result = _sut.Compute(
            new CoordinateDto { X = 0, Y = 0, Z = 1100 },
            [new StandardBallDto { BallId = "B1", KnownX = 326, KnownY = 228, KnownZ = 115 }],
            [new MeasuredBall { Index = 0, X = 326.71, Y = 228.05, Z = 113.08, Diameter = 25, IsWithinTolerance = true }]);

        Assert.Equal(1098.08, result.ScannerToFloorZ, 2);
        Assert.Equal(1098.08, result.ActualScannerPosition.Z, 2);
        Assert.Equal(-1.92, result.Compensation.OffsetZ, 2);
    }

    /// <summary>預期 1100、量到 1089 → 補償 Z = −11。</summary>
    [Fact]
    public void SingleBall_FloorModel_OffsetZWhenScannerLowerThanExpected()
    {
        var result = _sut.Compute(
            new CoordinateDto { X = 0, Y = 0, Z = 1100 },
            [new StandardBallDto { BallId = "B1", KnownX = 326, KnownY = 228, KnownZ = 115 }],
            [new MeasuredBall { Index = 0, X = 326.71, Y = 228.05, Z = 104, Diameter = 25, IsWithinTolerance = true }]);

        Assert.Equal(1089, result.ScannerToFloorZ, 1);
        Assert.Equal(-11, result.Compensation.OffsetZ, 1);
    }

    /// <summary>現場成功案例：反推 XY 應約 (99.88, 99.65)，非 3D Kabsch 污染的 (411, 338)。</summary>
    [Fact]
    public void TwoBalls_SuccessRun_ActualScannerXY_Near100()
    {
        var result = _sut.Compute(
            new CoordinateDto { X = 0, Y = 0, Z = 1100 },
            [
                new StandardBallDto { BallId = "BALL-01", KnownX = 283, KnownY = 508, KnownZ = 115 },
                new StandardBallDto { BallId = "BALL-02", KnownX = 449, KnownY = 289, KnownZ = 115 },
            ],
            [
                new MeasuredBall { Index = 0, X = 183.24282070322852, Y = 408.19348124805447, Z = 102.2886431758452, Diameter = 40.8, IsWithinTolerance = true },
                new MeasuredBall { Index = 1, X = 349.00177613076767, Y = 189.50716785579695, Z = 106.48544404056408, Diameter = 40.4, IsWithinTolerance = true },
            ]);

        Assert.True(Math.Abs(result.ActualScannerPosition!.X - 99.878) < 0.1);
        Assert.True(Math.Abs(result.ActualScannerPosition.Y - 99.650) < 0.1);
        Assert.True(result.ActualScannerPosition.X < 200);
        Assert.Equal(1089.387, result.ScannerToFloorZ, 2);
    }

    /// <summary>現場兩球範例：球距誤差 12.73 mm → RMSE 12.73。</summary>
    [Fact]
    public void TwoBalls_UserExample_DistanceRmse12_73()
    {
        var result = _sut.Compute(
            new CoordinateDto { X = 0, Y = 0, Z = 1100 },
            [
                new StandardBallDto { BallId = "BALL-01", KnownX = 183, KnownY = 409, KnownZ = 115 },
                new StandardBallDto { BallId = "BALL-02", KnownX = 326, KnownY = 160, KnownZ = 115 },
            ],
            [
                new MeasuredBall { Index = 0, X = 183.19097638500423, Y = 409.58802968911385, Z = 102.69, Diameter = 40.9, IsWithinTolerance = true },
                new MeasuredBall { Index = 1, X = 348.8860383618298, Y = 190.85417847146906, Z = 106.51, Diameter = 40.7, IsWithinTolerance = true },
            ]);

        Assert.Equal(12.73, result.Rmse, 2);
        Assert.Equal(12.73, result.MatchedBalls[0].ResidualMm, 2);
        Assert.Equal(12.73, result.MatchedBalls[1].ResidualMm, 2);
    }

    /// <summary>球距誤差 2 mm 時 RMSE 應 ≥ 1 mm 門檻。</summary>
    [Fact]
    public void TwoBalls_WithPairingError_RmseExceedsThreshold()
    {
        var result = _sut.Compute(
            new CoordinateDto { X = 0, Y = 0, Z = 0 },
            [
                new StandardBallDto { BallId = "B1", KnownX = 0, KnownY = 0, KnownZ = 0 },
                new StandardBallDto { BallId = "B2", KnownX = 100, KnownY = 0, KnownZ = 0 },
            ],
            [
                new MeasuredBall { Index = 0, X = 0, Y = 0, Z = 0, Diameter = 24.9, IsWithinTolerance = true },
                new MeasuredBall { Index = 1, X = 102, Y = 0, Z = 0, Diameter = 24.9, IsWithinTolerance = true },
            ]);

        Assert.True(result.Rmse >= InstrumentCalibrationService.RmseThresholdMm);
    }

    /// <summary>knownZ 與 measuredZ 差異大時，3D 配對可能錯配；XY 配對應仍正確且 RMSE≈0。</summary>
    [Fact]
    public void ThreeBalls_ZMismatch_StillPairsByXyAndLowRmse()
    {
        var result = _sut.Compute(
            new CoordinateDto { X = 0, Y = 0, Z = 1100 },
            [
                new StandardBallDto { BallId = "B1", KnownX = 0, KnownY = 0, KnownZ = 115 },
                new StandardBallDto { BallId = "B2", KnownX = 100, KnownY = 0, KnownZ = 115 },
                new StandardBallDto { BallId = "B3", KnownX = 200, KnownY = 0, KnownZ = 115 },
            ],
            [
                new MeasuredBall { Index = 0, X = 0, Y = 0, Z = 50, Diameter = 24.9, IsWithinTolerance = true },
                new MeasuredBall { Index = 1, X = 100, Y = 0, Z = 200, Diameter = 24.9, IsWithinTolerance = true },
                new MeasuredBall { Index = 2, X = 200, Y = 0, Z = 80, Diameter = 24.9, IsWithinTolerance = true },
            ]);

        Assert.True(result.Rmse < 1e-6);
        Assert.Equal("B1", result.MatchedBalls[0].BallId);
        Assert.Equal(0, result.MatchedBalls[0].MeasuredX, 3);
        Assert.Equal("B3", result.MatchedBalls[2].BallId);
        Assert.Equal(200, result.MatchedBalls[2].MeasuredX, 3);
    }

    /// <summary>補償恆等式：offset = actualScannerPosition − cameraExpectedPosition。</summary>
    [Fact]
    public void Compensation_OffsetEqualsActualMinusExpected()
    {
        var expected = new CoordinateDto { X = 1000, Y = 2000, Z = 1100 };
        var result = _sut.Compute(
            expected,
            [
                new StandardBallDto { BallId = "B1", KnownX = 1183, KnownY = 2409, KnownZ = 115 },
                new StandardBallDto { BallId = "B2", KnownX = 1349, KnownY = 2191, KnownZ = 115 },
            ],
            [
                new MeasuredBall { Index = 0, X = 183.2, Y = 409.6, Z = 102.7, Diameter = 40.9, IsWithinTolerance = true },
                new MeasuredBall { Index = 1, X = 348.9, Y = 190.9, Z = 106.5, Diameter = 40.7, IsWithinTolerance = true },
            ]);

        Assert.Equal(result.ActualScannerPosition!.X - expected.X, result.Compensation.OffsetX, 6);
        Assert.Equal(result.ActualScannerPosition.Y - expected.Y, result.Compensation.OffsetY, 6);
        Assert.Equal(result.ScannerToFloorZ - expected.Z, result.Compensation.OffsetZ, 6);
    }

    /// <summary>單球 XY：d = known − measured，offset = d − expected。</summary>
    [Fact]
    public void SingleBall_OffsetXY_EqualsKnownMinusMeasuredMinusExpected()
    {
        var expected = new CoordinateDto { X = 100, Y = 200, Z = 50 };
        var knownX = 160.0;
        var knownY = 390.0;
        var measX = 10.0;
        var measY = 20.0;

        var result = _sut.Compute(
            expected,
            [new StandardBallDto { BallId = "B1", KnownX = knownX, KnownY = knownY, KnownZ = 12 }],
            [new MeasuredBall { Index = 0, X = measX, Y = measY, Z = 2, Diameter = 24.9, IsWithinTolerance = true }]);

        Assert.Equal(knownX - measX - expected.X, result.Compensation.OffsetX, 6);
        Assert.Equal(knownY - measY - expected.Y, result.Compensation.OffsetY, 6);
        Assert.Equal(knownX - measX, result.ActualScannerPosition!.X, 6);
        Assert.Equal(knownY - measY, result.ActualScannerPosition.Y, 6);
    }

    /// <summary>多球 Z：scannerToFloorZ 為各球 c.z−(b.z−a.z) 之平均。</summary>
    [Fact]
    public void TwoBalls_ScannerToFloorZ_AveragesPerBallFloorModel()
    {
        var expected = new CoordinateDto { X = 0, Y = 0, Z = 1100 };
        var balls = new[]
        {
            (KnownZ: 115.0, MeasuredZ: 102.69),
            (KnownZ: 115.0, MeasuredZ: 106.51),
        };

        var result = _sut.Compute(
            expected,
            [
                new StandardBallDto { BallId = "B1", KnownX = 183, KnownY = 409, KnownZ = balls[0].KnownZ },
                new StandardBallDto { BallId = "B2", KnownX = 349, KnownY = 191, KnownZ = balls[1].KnownZ },
            ],
            [
                new MeasuredBall { Index = 0, X = 183.2, Y = 409.6, Z = balls[0].MeasuredZ, Diameter = 40.9, IsWithinTolerance = true },
                new MeasuredBall { Index = 1, X = 348.9, Y = 190.9, Z = balls[1].MeasuredZ, Diameter = 40.7, IsWithinTolerance = true },
            ]);

        var perBall = balls.Select(b => b.MeasuredZ - (b.KnownZ - expected.Z)).ToArray();
        var avg = perBall.Average();
        Assert.Equal(avg, result.ScannerToFloorZ, 4);
        Assert.Equal(avg - expected.Z, result.Compensation.OffsetZ, 4);
    }

    /// <summary>knownZ 與 measuredZ 不一致時，RMSE 仍只反映 XY。</summary>
    [Fact]
    public void TwoBalls_ZMismatch_DoesNotAffectRmse()
    {
        var result = _sut.Compute(
            new CoordinateDto { X = 0, Y = 0, Z = 1100 },
            [
                new StandardBallDto { BallId = "B1", KnownX = 0, KnownY = 0, KnownZ = 115 },
                new StandardBallDto { BallId = "B2", KnownX = 100, KnownY = 0, KnownZ = 115 },
            ],
            [
                new MeasuredBall { Index = 0, X = 0, Y = 0, Z = 50, Diameter = 24.9, IsWithinTolerance = true },
                new MeasuredBall { Index = 1, X = 100, Y = 0, Z = 200, Diameter = 24.9, IsWithinTolerance = true },
            ]);

        Assert.True(result.Rmse < 1e-6);
    }
}
