namespace ThreeDScan.Infrastructure.Export.Models;

/// <summary>實體平面。</summary>
/// <remarks>由 Hank Scan3D 的 SolidPlane 搬入，邏輯不改。</remarks>
public sealed class SolidPlane
{
    /// <summary>建立平面。</summary>
    /// <param name="origin">原點。</param>
    /// <param name="normal">法線。</param>
    public SolidPlane(ScanVector origin, ScanVector normal)
    {
        Origin = origin;
        Normal = normal;
    }

    /// <summary>原點。</summary>
    public ScanVector Origin { get; init; }

    /// <summary>法線。</summary>
    public ScanVector Normal { get; init; }
}
