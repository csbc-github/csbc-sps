namespace ThreeDScan.Application.Calibration.Models;

/// <summary>
/// 測試模式固定回應（不連掃描器）。數值取自一次實際成功的兩次拍攝。
/// </summary>
internal static class InstrumentCalibrationTestFixture
{
    /// <summary>第一次：回 taskId／seq=1，不拍攝。</summary>
    public static InstrumentCalibrationData CreateFirstShot(string scannerId, string taskId, string traceId)
    {
        return new InstrumentCalibrationData
        {
            ScannerId = scannerId,
            TraceId = traceId,
            TaskId = taskId,
            Seq = 1,
            Status = InstrumentCalibrationData.StatusAwaitingNextShot,
            RmseXY = 0.080,
            ScannerToFloorZ = 837.093,
            ActualScannerPosition = new CoordinateDto
            {
                X = 210.423,
                Y = 408.990,
                Z = 837.093,
            },
            Compensation = new CompensationDto
            {
                OffsetX = 120.423,
                OffsetY = 313.990,
                OffsetZ = -262.907,
            },
            Balls = FirstShotBalls(),
            MatchedBalls = FirstShotMatchedBalls(),
        };
    }

    /// <summary>第二次：回補償／rmseXY／zDeviation，不拍攝、不寫補償。</summary>
    public static InstrumentCalibrationData CreateSecondShot(string scannerId, string taskId, string traceId)
    {
        var calibratedAt = DateTimeOffset.Now.ToString("O");
        return new InstrumentCalibrationData
        {
            ScannerId = scannerId,
            TraceId = traceId,
            TaskId = taskId,
            Seq = 2,
            CalibratedAt = calibratedAt,
            RmseXY = 0.080,
            ZDeviation = 0.277,
            ScannerToFloorZ = 836.955,
            ActualScannerPosition = new CoordinateDto
            {
                X = 207.279,
                Y = 410.698,
                Z = 836.955,
            },
            Compensation = new CompensationDto
            {
                OffsetX = 117.279,
                OffsetY = 315.698,
                OffsetZ = -263.045,
            },
            Balls = SecondShotBalls(),
            MatchedBalls = SecondShotMatchedBalls(),
        };
    }

    private static IReadOnlyList<MeasuredBallDto> FirstShotBalls()
    {
        return new[]
        {
            Ball(0, 270.69, 286.39, 54.16, 25.286),
            Ball(1, 466.11, 368.39, 61.15, 24.553),
            Ball(2, 533.91, 168.18, 59.73, 24.340),
        };
    }

    private static IReadOnlyList<MeasuredBallDto> SecondShotBalls()
    {
        return new[]
        {
            Ball(0, 273.72, 290.28, 108.20, 25.051),
            Ball(1, 535.48, 168.78, 114.11, 24.864),
            Ball(2, 470.13, 369.73, 114.55, 24.265),
        };
    }

    private static IReadOnlyList<MatchedBallDto> FirstShotMatchedBalls()
    {
        return new[]
        {
            Match("BALL-01", 270.69, 286.39, 54.16, 481.48, 694.51, 321.25, 0.08),
            Match("BALL-02", 533.91, 168.18, 59.73, 744.96, 576.74, 321.25, 0.08),
            Match("BALL-03", 466.11, 368.39, 61.15, 676.83, 776.70, 321.25, 0.08),
        };
    }

    private static IReadOnlyList<MatchedBallDto> SecondShotMatchedBalls()
    {
        return new[]
        {
            Match("BALL-01", 273.72, 290.28, 108.20, 481.48, 694.51, 375.47, 0.08),
            Match("BALL-02", 535.48, 168.78, 114.11, 744.96, 576.74, 375.47, 0.08),
            Match("BALL-03", 470.13, 369.73, 114.55, 676.83, 776.70, 375.47, 0.08),
        };
    }

    private static MeasuredBallDto Ball(int index, double x, double y, double z, double diameter)
    {
        return new MeasuredBallDto
        {
            Index = index,
            X = x,
            Y = y,
            Z = z,
            Diameter = diameter,
            Ok = true,
        };
    }

    private static MatchedBallDto Match(
        string ballId,
        double mx,
        double my,
        double mz,
        double kx,
        double ky,
        double kz,
        double residual)
    {
        return new MatchedBallDto
        {
            BallId = ballId,
            MeasuredX = mx,
            MeasuredY = my,
            MeasuredZ = mz,
            KnownX = kx,
            KnownY = ky,
            KnownZ = kz,
            ResidualMm = residual,
        };
    }
}
