namespace ThreeDScan.Application.Calibration.Models;

/// <summary>單顆標準球與量測球的配對結果。</summary>
public sealed class MatchedBallDto
{
    /// <summary>台船傳入的球識別碼。</summary>
    public string BallId { get; init; } = "";

    /// <summary>量測球心 X (掃描器座標系, mm)。</summary>
    public double MeasuredX { get; init; }

    /// <summary>量測球心 Y (掃描器座標系, mm)。</summary>
    public double MeasuredY { get; init; }

    /// <summary>量測球心 Z (掃描器座標系, mm)。</summary>
    public double MeasuredZ { get; init; }

    /// <summary>已知絕對座標 X (mm)。</summary>
    public double KnownX { get; init; }

    /// <summary>已知絕對座標 Y (mm)。</summary>
    public double KnownY { get; init; }

    /// <summary>已知絕對座標 Z (mm)。</summary>
    public double KnownZ { get; init; }

    /// <summary>該顆球參與之球距誤差最大值 (mm)；單球為 0。</summary>
    public double ResidualMm { get; init; }
}
