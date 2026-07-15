namespace ThreeDScan.Domain.Models;

/// <summary>Ref 建立（SetupReference）量測結果，供寫入 ScannerConfig。</summary>
public sealed class ReferenceSetupResult
{
    public required string ScannerId { get; init; }
    public double RefPatternX { get; init; }
    public double RefPatternY { get; init; }
    public double RefSphereX { get; init; }
    public double RefSphereY { get; init; }
    public double DiscoveredDiameter { get; init; }
    public required string Message { get; init; }
    public required string ContentsFolder { get; init; }
}
