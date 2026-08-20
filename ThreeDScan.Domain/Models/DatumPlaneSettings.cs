namespace ThreeDScan.Domain.Models;

/// <summary>基準面（滾輪）平面量測 ROI，對應 ScannerConfig DatumPlane* 欄位。</summary>
public sealed class DatumPlaneSettings
{
    public bool Enabled { get; init; } = true;

    public double CenterX { get; init; } = 160;

    public double CenterY { get; init; } = 120;

    public double Width { get; init; } = 120;

    public double Height { get; init; } = 80;

    public double Angle { get; init; }
}
