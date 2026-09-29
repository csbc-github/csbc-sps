namespace ThreeDScan.Domain.Models;

/// <summary>LJ 平面工具量測滾輪／基準面結果（測試用）。</summary>
public sealed class DatumPlaneMeasureResult
{
    /// <summary>是否已執行平面量測。</summary>
    public bool Measured { get; init; }

    /// <summary>量測是否成功。</summary>
    public bool Ok { get; init; }

    /// <summary>失敗時的說明。</summary>
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
