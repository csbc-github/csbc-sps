namespace ThreeDScan.Infrastructure.Export.Models;

/// <summary>z = Ax + By + C 平面。</summary>
/// <remarks>由 Hank Scan3D 的 FittedSurface 搬入，邏輯不改。</remarks>
public sealed class FittedSurface
{
    /// <summary>建立平面。</summary>
    /// <param name="a">A。</param>
    /// <param name="b">B。</param>
    /// <param name="c">C。</param>
    /// <param name="rmsMm">RMS（mm）。</param>
    /// <param name="inlierCount">內點數。</param>
    /// <param name="sampleCount">取樣數。</param>
    public FittedSurface(double a, double b, double c, double rmsMm, int inlierCount, int sampleCount)
    {
        A = a;
        B = b;
        C = c;
        RmsMm = rmsMm;
        InlierCount = inlierCount;
        SampleCount = sampleCount;
    }

    /// <summary>A。</summary>
    public double A { get; init; }

    /// <summary>B。</summary>
    public double B { get; init; }

    /// <summary>C。</summary>
    public double C { get; init; }

    /// <summary>RMS（mm）。</summary>
    public double RmsMm { get; init; }

    /// <summary>內點數。</summary>
    public int InlierCount { get; init; }

    /// <summary>取樣數。</summary>
    public int SampleCount { get; init; }
}
