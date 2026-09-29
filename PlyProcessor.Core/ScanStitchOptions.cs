namespace PlyProcessor.Core;

/// <summary>多段點雲沿 X 拼接的參數。</summary>
public sealed class ScanStitchOptions
{
    public ScanStitchOptions()
    {
        GapMm = 0f;
        TrimPercent = 3f;
        AlignYZ = true;
        ZFloor = true;
    }

    /// <summary>段與段之間的 X 間距（mm）。</summary>
    public float GapMm { get; init; }

    /// <summary>各段依 X 範圍裁掉兩端的百分比。</summary>
    public float TrimPercent { get; init; }

    /// <summary>後段 Y／Z 中位數對齊第一段。</summary>
    public bool AlignYZ { get; init; }

    /// <summary>全體最低 Z 歸零。</summary>
    public bool ZFloor { get; init; }
}
