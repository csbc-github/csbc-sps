namespace ThreeDScan.Infrastructure.Export.Models;

/// <summary>截面輪廓點。</summary>
/// <remarks>由 Hank Scan3D 的 ProfilePoint 搬入，邏輯不改。</remarks>
public readonly struct ProfilePoint
{
    /// <summary>建立輪廓點。</summary>
    /// <param name="u">橫向 U（mm）。</param>
    /// <param name="z">高度 Z（mm）。</param>
    /// <param name="support">支持點數。</param>
    public ProfilePoint(double u, double z, int support)
    {
        U = u;
        Z = z;
        Support = support;
    }

    /// <summary>橫向 U（mm）。</summary>
    public double U { get; }

    /// <summary>高度 Z（mm）。</summary>
    public double Z { get; }

    /// <summary>支持點數。</summary>
    public int Support { get; }
}
