namespace ThreeDScan.Domain.Models;

/// <summary>Keyence 影像座標轉換（對齊 LHeightImage ImagePosition / Coefficient）。</summary>
public sealed class CaptureImageGeometry
{
    public int Width { get; init; }
    public int Height { get; init; }
    public double OriginX { get; init; }
    public double OriginY { get; init; }
    public double CoefficientX { get; init; }
    public double CoefficientY { get; init; }

    public double ToPixelX(double physicalX)
    {
        return CoefficientX != 0 ? (physicalX - OriginX) / CoefficientX : 0;
    }

    public double ToPixelY(double physicalY)
    {
        return CoefficientY != 0 ? (physicalY - OriginY) / CoefficientY : 0;
    }
}
