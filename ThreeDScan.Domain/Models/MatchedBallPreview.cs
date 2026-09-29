namespace ThreeDScan.Domain.Models;

/// <summary>SignalR 推送的配對球摘要。</summary>
public sealed class MatchedBallPreview
{
    /// <summary>標準球識別。</summary>
    public string BallId { get; init; } = "";

    /// <summary>已知絕對座標 X (mm)。</summary>
    public double KnownX { get; init; }

    /// <summary>已知絕對座標 Y (mm)。</summary>
    public double KnownY { get; init; }

    /// <summary>量測座標 X (mm)。</summary>
    public double MeasuredX { get; init; }

    /// <summary>量測座標 Y (mm)。</summary>
    public double MeasuredY { get; init; }

    /// <summary>殘差 (mm)。</summary>
    public double ResidualMm { get; init; }
}
