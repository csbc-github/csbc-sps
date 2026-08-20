namespace ThreeDScan.Domain.Models;

/// <summary>SignalR 推送的配對球摘要。</summary>
public sealed class MatchedBallPreview
{
    public string BallId { get; init; } = "";
    public double KnownX { get; init; }
    public double KnownY { get; init; }
    public double MeasuredX { get; init; }
    public double MeasuredY { get; init; }
    public double ResidualMm { get; init; }
}
