namespace ThreeDScan.Infrastructure.Export;

/// <summary>拼接段顏色。</summary>
/// <remarks>由 Hank Scan3D 的 StitchPalette 搬入，邏輯不改。</remarks>
public static class StitchPalette
{
    private static readonly (byte R, byte G, byte B)[] Colors =
    [
        (38, 190, 172), (239, 151, 73), (97, 159, 244), (199, 132, 224),
        (223, 196, 77), (235, 113, 141), (139, 197, 92), (119, 200, 224),
    ];

    /// <summary>依段索引取 RGB。</summary>
    /// <remarks>來自 Hank Scan3D 的 <c>Scan3D.Processing.Legacy.Stitching.StitchPalette</c>。</remarks>
    /// <param name="scanIndex">段索引。</param>
    public static (byte R, byte G, byte B) Rgb(int scanIndex)
    {
        return Colors[scanIndex % Colors.Length];
    }
}
