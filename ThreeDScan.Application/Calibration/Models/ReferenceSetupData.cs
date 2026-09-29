namespace ThreeDScan.Application.Calibration.Models;

/// <summary>POST /api/calibration/reference 成功回應資料。</summary>
public sealed class ReferenceSetupData
{
    /// <summary>掃描器編號。</summary>
    public required string ScannerId { get; init; }

    /// <summary>追溯 ID。</summary>
    public required string TraceId { get; init; }

    /// <summary>本次使用的程式編號。</summary>
    public byte ProgramNo { get; init; }

    /// <summary>Ref 圖樣 X (mm)。</summary>
    public double RefPatternX { get; init; }

    /// <summary>Ref 圖樣 Y (mm)。</summary>
    public double RefPatternY { get; init; }

    /// <summary>量得的球徑 (mm)。</summary>
    public double DiscoveredDiameter { get; init; }

    /// <summary>Contents 資料夾路徑。</summary>
    public required string ContentsFolder { get; init; }

    /// <summary>結果說明。</summary>
    public required string Message { get; init; }
}
