namespace ThreeDScan.Infrastructure.Export.Models;

/// <summary>掃描器 XY 間距。</summary>
/// <remarks>由 Hank Scan3D 的 ScannerPitch 搬入，邏輯不改。</remarks>
public sealed class ScannerPitch
{
    /// <summary>建立間距。</summary>
    /// <param name="xMm">X 間距（mm）。</param>
    /// <param name="yMm">Y 間距（mm）。</param>
    public ScannerPitch(double xMm, double yMm)
    {
        XMm = xMm;
        YMm = yMm;
    }

    /// <summary>X 間距（mm）。</summary>
    public double XMm { get; init; }

    /// <summary>Y 間距（mm）。</summary>
    public double YMm { get; init; }
}
