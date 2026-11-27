namespace ThreeDScan.Domain.Models;

/// <summary>Hole-only OneClick-compatible raw capture, before opening extraction.</summary>
public sealed class HoleRawCaptureResult
{
    public required string CaptureId { get; init; }
    public required string RawPlyPath { get; init; }
    public required SegmentCaptureMetadata Metadata { get; init; }
    public bool IsReplay { get; init; }
}
