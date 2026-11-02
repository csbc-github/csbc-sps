namespace ThreeDScan.Infrastructure.Export.Models;

/// <summary>實體三角形。</summary>
/// <remarks>由 Hank Scan3D 的 SolidTriangle 搬入，邏輯不改。</remarks>
public readonly struct SolidTriangle
{
    /// <summary>建立三角形。</summary>
    /// <param name="a">頂點 A。</param>
    /// <param name="b">頂點 B。</param>
    /// <param name="c">頂點 C。</param>
    /// <param name="planeGroup">平面群組。</param>
    /// <param name="solid">實體編號。</param>
    public SolidTriangle(int a, int b, int c, int planeGroup, int solid)
    {
        A = a;
        B = b;
        C = c;
        PlaneGroup = planeGroup;
        Solid = solid;
    }

    /// <summary>頂點 A。</summary>
    public int A { get; }

    /// <summary>頂點 B。</summary>
    public int B { get; }

    /// <summary>頂點 C。</summary>
    public int C { get; }

    /// <summary>平面群組。</summary>
    public int PlaneGroup { get; }

    /// <summary>實體編號。</summary>
    public int Solid { get; }
}
