namespace ThreeDScan.Domain.Models;

/// <summary>基準面（滾輪）平面量測 ROI，對應 ScannerConfig DatumPlane* 欄位。</summary>
public sealed class DatumPlaneSettings
{
    /// <summary>是否啟用基準面量測。</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>ROI 中心 X (mm)。</summary>
    public double CenterX { get; init; } = 160;

    /// <summary>ROI 中心 Y (mm)。</summary>
    public double CenterY { get; init; } = 120;

    /// <summary>ROI 寬度 (mm)。</summary>
    public double Width { get; init; } = 120;

    /// <summary>ROI 高度 (mm)。</summary>
    public double Height { get; init; } = 80;

    /// <summary>ROI 旋轉角（度）。</summary>
    public double Angle { get; init; }
}
