namespace ThreeDScan.Domain.Models;

/// <summary>每台掃描器一筆 LJ Ref／量測參數（DB: dbo.ScannerConfig）；與拍攝群組無關。</summary>
public sealed class ScannerConfigRecord
{
    public required string ScannerId { get; init; }
    public double RefPatternX { get; init; }
    public double RefPatternY { get; init; }
    public double RefSphereX { get; init; }
    public double RefSphereY { get; init; }
    public required string ContentsFolder { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public string? UpdatedBy { get; init; }
    public string TraceId { get; init; } = "";

    /// <summary>預期球直徑 (mm)；NULL 時用 appsettings。</summary>
    public double? ExpectedBallDiameter { get; init; }

    public double? DiameterTolerance { get; init; }

    public double? MaxDiameterError { get; init; }

    public int? BallCount { get; init; }

    public bool? DatumPlaneEnabled { get; init; }

    public double? DatumPlaneCenterX { get; init; }

    public double? DatumPlaneCenterY { get; init; }

    public double? DatumPlaneWidth { get; init; }

    public double? DatumPlaneHeight { get; init; }

    public double? DatumPlaneAngle { get; init; }
}
