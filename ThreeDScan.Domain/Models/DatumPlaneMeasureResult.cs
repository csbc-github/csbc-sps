namespace ThreeDScan.Domain.Models;

/// <summary>LJ 平面工具量測滾輪／基準面結果（測試用）。</summary>
public sealed class DatumPlaneMeasureResult
{
    public bool Measured { get; init; }

    public bool Ok { get; init; }

    public string? ErrorMessage { get; init; }

    /// <summary>平面方程式係數 A（LJ PlaneResult）。</summary>
    public double PlaneA { get; init; }

    /// <summary>平面方程式係數 B。</summary>
    public double PlaneB { get; init; }

    /// <summary>平面方程式係數 C。</summary>
    public double PlaneC { get; init; }

    /// <summary>ROI 中心處基準面 Z（由平面係數估算，mm）。</summary>
    public double? PlaneZAtRegionCenter { get; init; }

    /// <summary>第一顆球心 Z − 該 XY 處基準面 Z（mm）。</summary>
    public double? FirstBallRelativeZ { get; init; }
}
