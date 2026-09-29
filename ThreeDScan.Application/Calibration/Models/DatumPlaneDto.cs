namespace ThreeDScan.Application.Calibration.Models;

/// <summary>基準面（滾輪）平面量測結果。</summary>
public sealed class DatumPlaneDto
{
    /// <summary>量測是否成功。</summary>
    public bool Ok { get; init; }

    /// <summary>失敗時的說明。</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>平面方程式係數 A。</summary>
    public double PlaneA { get; init; }

    /// <summary>平面方程式係數 B。</summary>
    public double PlaneB { get; init; }

    /// <summary>平面方程式係數 C。</summary>
    public double PlaneC { get; init; }

    /// <summary>ROI 中心處基準面 Z（mm）。</summary>
    public double? PlaneZAtRegionCenter { get; init; }

    /// <summary>第一顆球心相對基準面高度（mm）。</summary>
    public double? FirstBallRelativeZ { get; init; }
}
