namespace ThreeDScan.Application.Calibration.Models;

/// <summary>基準面（滾輪）平面量測結果。</summary>
public sealed class DatumPlaneDto
{
    public bool Ok { get; init; }

    public string? ErrorMessage { get; init; }

    public double PlaneA { get; init; }

    public double PlaneB { get; init; }

    public double PlaneC { get; init; }

    /// <summary>ROI 中心處基準面 Z（mm）。</summary>
    public double? PlaneZAtRegionCenter { get; init; }

    /// <summary>第一顆球心相對基準面高度（mm）。</summary>
    public double? FirstBallRelativeZ { get; init; }
}
