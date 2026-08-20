using ThreeDScan.Application.Calibration.Models;
using ThreeDScan.Domain.Models;

namespace ThreeDScan.Application.Calibration;

/// <summary>
/// UC-01 儀器校正領域服務：比對台船傳入的標準球絕對座標與掃描器量測座標，
/// 反推掃描器實際位置並計算補償值（SD SA §9.1）；RMSE 評估標準球間 XY 相對距離誤差。
/// </summary>
public sealed class InstrumentCalibrationService
{
    /// <summary>校正殘差門檻 (mm)；超過視為 CAL_FEED_003，不寫入補償。</summary>
    public const double RmseThresholdMm = 1.0;

    /// <summary>
    /// 執行儀器校正計算：配對 → 剛體變換 → 補償 offset。
    /// </summary>
    /// <param name="expectedPosition">掃描器預期絕對位置（台船傳入）。</param>
    /// <param name="standardBalls">標準球已知絕對座標；數量以台船傳入為準。</param>
    /// <param name="measuredBalls">掃描器量測到的球心（掃描器座標系）。</param>
    /// <returns>補償值、實際掃描器位置與配對明細。</returns>
    /// <exception cref="InstrumentCalibrationException">球數不足或配對失敗時拋出 CAL_FEED_001。</exception>
    public InstrumentCalibrationComputeResult Compute(
        CoordinateDto expectedPosition,
        IReadOnlyList<StandardBallDto> standardBalls,
        IReadOnlyList<MeasuredBall> measuredBalls)
    {
        if (standardBalls.Count == 0)
            throw new InstrumentCalibrationException("CAL_FEED_001", "未提供標準球已知座標。");

        if (measuredBalls.Count < standardBalls.Count)
            throw new InstrumentCalibrationException(
                "CAL_FEED_001",
                $"標準球未偵測到或無法識別：需 {standardBalls.Count} 顆，僅量得 {measuredBalls.Count} 顆。");

        var pairs = PairBalls(standardBalls, measuredBalls);
        var actualXY = EstimateScannerPositionXY(pairs);
        var rmse = ComputePairwiseDistanceRmse(pairs);
        var perBallPairErrors = ComputePerBallMaxPairDistanceError(pairs);

        var expected = ToVec(expectedPosition);
        var scannerToFloorZ = ComputeScannerToFloorZ(expectedPosition, pairs);
        var offset = new Vec3(
            actualXY.X - expected.X,
            actualXY.Y - expected.Y,
            scannerToFloorZ - expected.Z);

        var matched = pairs.Select((p, i) => new MatchedBallDto
        {
            BallId = p.BallId,
            MeasuredX = p.Measured.X,
            MeasuredY = p.Measured.Y,
            MeasuredZ = p.Measured.Z,
            KnownX = p.Known.X,
            KnownY = p.Known.Y,
            KnownZ = p.Known.Z,
            ResidualMm = perBallPairErrors[i],
        }).ToList();

        return new InstrumentCalibrationComputeResult
        {
            Compensation = new CompensationDto
            {
                OffsetX = offset.X,
                OffsetY = offset.Y,
                OffsetZ = offset.Z,
            },
            Rmse = rmse,
            ActualScannerPosition = new CoordinateDto
            {
                X = actualXY.X,
                Y = actualXY.Y,
                Z = scannerToFloorZ,
            },
            ScannerToFloorZ = scannerToFloorZ,
            MatchedBalls = matched,
        };
    }

    /// <summary>
    /// 由量測球心與台船宣告推算掃描器到地板（滾輪面）距離 (mm)。
    /// 單球：c.z − (b.z − a.z)；多球取配對平均。
    /// </summary>
    private static double ComputeScannerToFloorZ(
        CoordinateDto expectedPosition,
        IReadOnlyList<BallPair> pairs)
    {
        var sum = 0.0;
        foreach (var p in pairs)
            sum += p.Measured.Z - (p.Known.Z - expectedPosition.Z);
        return sum / pairs.Count;
    }

    /// <summary>
    /// 將每顆標準球與量測球 1:1 配對（XY 質心粗對齊後，以最小總 XY 距離最佳指派）。
    /// Z 不參與配對：knownZ 為機台絕對高度，measuredZ 為掃描器座標，兩者不可直接比較。
    /// </summary>
    private static List<BallPair> PairBalls(
        IReadOnlyList<StandardBallDto> standardBalls,
        IReadOnlyList<MeasuredBall> measuredBalls)
    {
        var n = standardBalls.Count;
        var m = measuredBalls.Count;
        var known = standardBalls.Select(b => new Vec3(b.KnownX, b.KnownY, b.KnownZ)).ToArray();
        var measured = measuredBalls.Select(b => new Vec3(b.X, b.Y, b.Z)).ToArray();

        var shiftX = known.Average(k => k.X) - measured.Average(p => p.X);
        var shiftY = known.Average(k => k.Y) - measured.Average(p => p.Y);

        var cost = new double[n, m];
        for (var i = 0; i < n; i++)
        for (var j = 0; j < m; j++)
        {
            var dx = known[i].X - (measured[j].X + shiftX);
            var dy = known[i].Y - (measured[j].Y + shiftY);
            cost[i, j] = Math.Sqrt(dx * dx + dy * dy);
        }

        var assignment = SolveMinAssignment(cost, n, m);
        if (assignment.Any(j => j < 0))
            throw new InstrumentCalibrationException("CAL_FEED_001", "標準球配對失敗。");

        var pairs = new List<BallPair>(n);
        for (var i = 0; i < n; i++)
            pairs.Add(new BallPair(standardBalls[i].BallId, known[i], measured[assignment[i]]));

        return pairs;
    }

    /// <summary>矩形指派：n 列各選一個不重複欄，使 cost 總和最小（n ≤ m，n,m ≤ 10）。</summary>
    private static int[] SolveMinAssignment(double[,] cost, int n, int m)
    {
        var best = new int[n];
        Array.Fill(best, -1);
        var bestSum = double.MaxValue;
        var used = new bool[m];
        var current = new int[n];
        Search(0, 0);

        return best;

        void Search(int row, double sum)
        {
            if (row == n)
            {
                if (sum < bestSum)
                {
                    bestSum = sum;
                    Array.Copy(current, best, n);
                }

                return;
            }

            for (var col = 0; col < m; col++)
            {
                if (used[col]) continue;
                used[col] = true;
                current[row] = col;
                Search(row + 1, sum + cost[row, col]);
                used[col] = false;
            }
        }
    }

    /// <summary>
    /// 反推掃描器實際水平位置 (X,Y)：僅用配對球的 x/y 做 2D 剛體對齊。
    /// knownZ 與 measuredZ 語意不同，不可納入 3D Kabsch，否則會污染 XY。
    /// 單球：known.xy − measured.xy；多球：2D Kabsch 平移（含水平旋轉）。
    /// </summary>
    private static Vec3 EstimateScannerPositionXY(IReadOnlyList<BallPair> pairs)
    {
        if (pairs.Count == 1)
        {
            var p = pairs[0];
            return new Vec3(p.Known.X - p.Measured.X, p.Known.Y - p.Measured.Y, 0);
        }

        var cmX = 0.0; var cmY = 0.0; var ckX = 0.0; var ckY = 0.0;
        foreach (var p in pairs)
        {
            cmX += p.Measured.X; cmY += p.Measured.Y;
            ckX += p.Known.X; ckY += p.Known.Y;
        }

        var n = pairs.Count;
        cmX /= n; cmY /= n; ckX /= n; ckY /= n;

        var h00 = 0.0; var h01 = 0.0; var h10 = 0.0; var h11 = 0.0;
        foreach (var p in pairs)
        {
            var mx = p.Measured.X - cmX;
            var my = p.Measured.Y - cmY;
            var kx = p.Known.X - ckX;
            var ky = p.Known.Y - ckY;
            h00 += mx * kx;
            h01 += mx * ky;
            h10 += my * kx;
            h11 += my * ky;
        }

        var theta = Math.Atan2(h10 - h01, h00 + h11);
        var c = Math.Cos(theta);
        var s = Math.Sin(theta);
        var tx = ckX - (c * cmX - s * cmY);
        var ty = ckY - (s * cmX + c * cmY);
        return new Vec3(tx, ty, 0);
    }

    /// <summary>
    /// 球距 RMSE：所有配對球兩兩 XY 距離誤差之均方根 (mm)。
    /// 單球無法驗證相對幾何，回傳 0。
    /// </summary>
    private static double ComputePairwiseDistanceRmse(IReadOnlyList<BallPair> pairs)
    {
        var errors = CollectPairwiseDistanceErrors(pairs);
        if (errors.Count == 0)
            return 0;

        var sumSq = errors.Sum(e => e * e);
        return Math.Sqrt(sumSq / errors.Count);
    }

    /// <summary>每顆球參與之球距誤差的最大值 (mm)；單球為 0。</summary>
    private static double[] ComputePerBallMaxPairDistanceError(IReadOnlyList<BallPair> pairs)
    {
        var n = pairs.Count;
        var max = new double[n];
        for (var i = 0; i < n; i++)
        for (var j = i + 1; j < n; j++)
        {
            var err = PairwiseDistanceError(pairs[i], pairs[j]);
            max[i] = Math.Max(max[i], err);
            max[j] = Math.Max(max[j], err);
        }

        return max;
    }

    private static List<double> CollectPairwiseDistanceErrors(IReadOnlyList<BallPair> pairs)
    {
        var errors = new List<double>();
        for (var i = 0; i < pairs.Count; i++)
        for (var j = i + 1; j < pairs.Count; j++)
            errors.Add(PairwiseDistanceError(pairs[i], pairs[j]));
        return errors;
    }

    private static double PairwiseDistanceError(BallPair a, BallPair b)
    {
        var knownDist = DistanceXY(a.Known, b.Known);
        var measuredDist = DistanceXY(a.Measured, b.Measured);
        return Math.Abs(knownDist - measuredDist);
    }

    /// <summary>兩點 XY 平面距離 (mm)。</summary>
    private static double DistanceXY(Vec3 a, Vec3 b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>將 API 座標 DTO 轉為內部三維向量。</summary>
    private static Vec3 ToVec(CoordinateDto c)
    {
        return new(c.X, c.Y, c.Z);
    }

    /// <summary>三維向量（內部計算用）。</summary>
    private readonly record struct Vec3(double X, double Y, double Z);

    /// <summary>配對後的標準球與量測球（含 BallId）。</summary>
    private readonly record struct BallPair(string BallId, Vec3 Known, Vec3 Measured);
}
