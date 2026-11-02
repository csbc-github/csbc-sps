namespace ThreeDScan.Infrastructure.Export.Models;

/// <summary>面內包圍盒。</summary>
/// <remarks>由 Hank Scan3D 的 BoundaryBox 搬入，邏輯不改。</remarks>
public sealed class BoundaryBox
{
    /// <summary>建立包圍盒。</summary>
    /// <param name="minUMm">U 最小。</param>
    /// <param name="maxUMm">U 最大。</param>
    /// <param name="minVMm">V 最小。</param>
    /// <param name="maxVMm">V 最大。</param>
    /// <param name="widthMm">寬（mm）。</param>
    /// <param name="heightMm">高（mm）。</param>
    public BoundaryBox(double minUMm, double maxUMm, double minVMm, double maxVMm, double widthMm, double heightMm)
    {
        MinUMm = minUMm;
        MaxUMm = maxUMm;
        MinVMm = minVMm;
        MaxVMm = maxVMm;
        WidthMm = widthMm;
        HeightMm = heightMm;
    }

    /// <summary>U 最小。</summary>
    public double MinUMm { get; init; }

    /// <summary>U 最大。</summary>
    public double MaxUMm { get; init; }

    /// <summary>V 最小。</summary>
    public double MinVMm { get; init; }

    /// <summary>V 最大。</summary>
    public double MaxVMm { get; init; }

    /// <summary>寬（mm）。</summary>
    public double WidthMm { get; init; }

    /// <summary>高（mm）。</summary>
    public double HeightMm { get; init; }
}
