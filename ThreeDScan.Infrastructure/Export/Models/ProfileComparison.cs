namespace ThreeDScan.Infrastructure.Export.Models;

/// <summary>兩段截面高度比較。</summary>
/// <remarks>由 Hank Scan3D 的 ProfileComparison 搬入，邏輯不改。</remarks>
public sealed class ProfileComparison
{
    /// <summary>建立比較。</summary>
    /// <param name="u">橫向 U（mm）。</param>
    /// <param name="heightA">A 高度（mm）。</param>
    /// <param name="heightB">B 高度（mm）。</param>
    /// <param name="deltaMm">A 減 B（mm）。</param>
    public ProfileComparison(double u, double heightA, double heightB, double deltaMm)
    {
        U = u;
        HeightA = heightA;
        HeightB = heightB;
        DeltaMm = deltaMm;
    }

    /// <summary>橫向 U（mm）。</summary>
    public double U { get; init; }

    /// <summary>A 高度（mm）。</summary>
    public double HeightA { get; init; }

    /// <summary>B 高度（mm）。</summary>
    public double HeightB { get; init; }

    /// <summary>A 減 B（mm）。</summary>
    public double DeltaMm { get; init; }
}
