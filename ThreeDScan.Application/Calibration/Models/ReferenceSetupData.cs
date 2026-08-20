namespace ThreeDScan.Application.Calibration.Models;

/// <summary>POST /api/calibration/reference 成功回應資料。</summary>
public sealed class ReferenceSetupData
{
    public required string ScannerId { get; init; }
    public required string TraceId { get; init; }
    public byte ProgramNo { get; init; }
    public double RefPatternX { get; init; }
    public double RefPatternY { get; init; }
    public double DiscoveredDiameter { get; init; }
    public required string ContentsFolder { get; init; }
    public required string Message { get; init; }
}
