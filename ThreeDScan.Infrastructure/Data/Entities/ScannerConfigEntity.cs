namespace ThreeDScan.Infrastructure.Data.Entities;

public sealed class ScannerConfigEntity
{
    public string ScannerId { get; set; } = "";
    public double RefPatternX { get; set; }
    public double RefPatternY { get; set; }
    public double RefSphereX { get; set; }
    public double RefSphereY { get; set; }
    public string ContentsFolder { get; set; } = "";
    public DateTimeOffset UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
    public string TraceId { get; set; } = "";
    public double? ExpectedBallDiameter { get; set; }
    public double? DiameterTolerance { get; set; }
    public double? MaxDiameterError { get; set; }
    public int? BallCount { get; set; }
    public bool? DatumPlaneEnabled { get; set; }
    public double? DatumPlaneCenterX { get; set; }
    public double? DatumPlaneCenterY { get; set; }
    public double? DatumPlaneWidth { get; set; }
    public double? DatumPlaneHeight { get; set; }
    public double? DatumPlaneAngle { get; set; }
}
