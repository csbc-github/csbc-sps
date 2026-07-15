using ThreeDScan.Application.Calibration.Dtos;

namespace ThreeDScan.Application.Calibration;

/// <summary>儀器校正 RMSE 失敗時的除錯說明。</summary>
internal static class InstrumentCalibrationDiagnostics
{
    public static string FormatRmseFailure(InstrumentCalibrationComputeResult computed)
    {
        var matched = computed.MatchedBalls;
        if (matched.Count == 0)
            return $"校正殘差 RMSE={computed.Rmse:F3} mm，超過門檻 {InstrumentCalibrationService.RmseThresholdMm} mm。";

        var lines = new List<string>
        {
            $"校正殘差 RMSE={computed.Rmse:F3} mm（球距誤差均方根），超過門檻 {InstrumentCalibrationService.RmseThresholdMm} mm。",
            "球距誤差明細：",
        };

        for (var i = 0; i < matched.Count; i++)
        for (var j = i + 1; j < matched.Count; j++)
        {
            var a = matched[i];
            var b = matched[j];
            var knownDist = SpanXY(a.KnownX, a.KnownY, b.KnownX, b.KnownY);
            var measuredDist = SpanXY(a.MeasuredX, a.MeasuredY, b.MeasuredX, b.MeasuredY);
            var err = Math.Abs(knownDist - measuredDist);
            lines.Add(
                $"  {a.BallId}↔{b.BallId}: known距={knownDist:F2} measured距={measuredDist:F2} 誤差={err:F2} mm");
        }

        lines.Add("RMSE 驗證標準球間相對距離，非單顆絕對座標；請核對台船 known 球距與現場配置。");
        return string.Join(Environment.NewLine, lines);
    }

    private static double SpanXY(double x1, double y1, double x2, double y2)
    {
        var dx = x2 - x1;
        var dy = y2 - y1;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
