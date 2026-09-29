namespace ThreeDScan.Domain.Models;

/// <summary>Keyence 影像座標轉換（對齊 LHeightImage ImagePosition / Coefficient）。</summary>
public sealed class CaptureImageGeometry
{
    /// <summary>影像寬度（像素）。</summary>
    public int Width { get; init; }

    /// <summary>影像高度（像素）。</summary>
    public int Height { get; init; }

    /// <summary>物理座標原點 X (mm)。</summary>
    public double OriginX { get; init; }

    /// <summary>物理座標原點 Y (mm)。</summary>
    public double OriginY { get; init; }

    /// <summary>X 方向 mm／像素係數。</summary>
    public double CoefficientX { get; init; }

    /// <summary>Y 方向 mm／像素係數。</summary>
    public double CoefficientY { get; init; }

    /// <summary>物理 X (mm) 轉像素 X。</summary>
    public double ToPixelX(double physicalX)
    {
        return CoefficientX != 0 ? (physicalX - OriginX) / CoefficientX : 0;
    }

    /// <summary>物理 Y (mm) 轉像素 Y。</summary>
    public double ToPixelY(double physicalY)
    {
        return CoefficientY != 0 ? (physicalY - OriginY) / CoefficientY : 0;
    }
}
