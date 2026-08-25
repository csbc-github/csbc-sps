namespace ThreeDScan.Domain.Models;

/// <summary>單次掃描擷取結果（暫存檔路徑與影像幾何）。</summary>
public sealed class ScannerCaptureResult
{
    public required string CaptureId { get; init; }
    public string? HeightImagePath { get; init; }
    public string? GrayImagePath { get; init; }
    public CaptureImageGeometry? Geometry { get; init; }
}
