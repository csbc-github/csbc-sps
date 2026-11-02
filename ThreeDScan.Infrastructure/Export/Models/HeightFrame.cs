namespace ThreeDScan.Infrastructure.Export.Models;

/// <summary>
/// 一次擷取的高度圖。對應 Hank <c>Scan3D.Scanner.HeightFrame</c>。
/// </summary>
public sealed class HeightFrame
{
    /// <summary>幾何與間距。</summary>
    public required HeightInfo Info { get; init; }

    /// <summary>SDK 影像編號。</summary>
    public uint ImageNo { get; init; }

    /// <summary>第一條剖面編號（寫 Y 時加到列索引）。</summary>
    public uint FirstProfileNo { get; init; }

    /// <summary>剖面列數。</summary>
    public int ProfileCount { get; init; }

    /// <summary>高度 raw（列優先，長度＝XPointCount×ProfileCount）。</summary>
    public required ushort[] Heights { get; init; }

    /// <summary>可選亮度（與 Heights 同長度）。</summary>
    public byte[]? Luminance { get; init; }
}
