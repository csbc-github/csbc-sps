namespace ThreeDScan.Domain.Models;

/// <summary>Ref 建立（SetupReference）量測結果，供寫入 ScannerConfig。</summary>
public sealed class ReferenceSetupResult
{
    /// <summary>掃描器編號。</summary>
    public required string ScannerId { get; init; }

    /// <summary>Ref 圖樣 X (mm)。</summary>
    public double RefPatternX { get; init; }

    /// <summary>Ref 圖樣 Y (mm)。</summary>
    public double RefPatternY { get; init; }

    /// <summary>Ref 球心 X (mm)。</summary>
    public double RefSphereX { get; init; }

    /// <summary>Ref 球心 Y (mm)。</summary>
    public double RefSphereY { get; init; }

    /// <summary>量得的球徑 (mm)。</summary>
    public double DiscoveredDiameter { get; init; }

    /// <summary>結果說明。</summary>
    public required string Message { get; init; }

    /// <summary>Contents 資料夾路徑。</summary>
    public required string ContentsFolder { get; init; }
}
