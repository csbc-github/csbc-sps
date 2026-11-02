namespace ThreeDScan.Infrastructure.Export.Models;

/// <summary>
/// 高度圖幾何（單位與 Hank <c>Scan3D.Scanner.HeightInfo</c> 相同：長度欄位為 ×100000 mm）。
/// </summary>
public sealed class HeightInfo
{
    /// <summary>X 方向點數。</summary>
    public ushort XPointCount { get; init; }

    /// <summary>Y 方向線數（SDK 欄位；寫 PLY 時以 <see cref="HeightFrame.ProfileCount"/> 為準）。</summary>
    public ushort YLineCount { get; init; }

    /// <summary>是否輸出亮度（1＝有 luminance 緩衝）。</summary>
    public byte LuminanceOutput { get; init; }

    /// <summary>掃描器座標 X 起點（÷100000 得 mm）。</summary>
    public int XStart { get; init; }

    /// <summary>X 間距（÷100000 得 mm）。</summary>
    public uint PitchX { get; init; }

    /// <summary>掃描器座標 Y 起點（÷100000 得 mm）。</summary>
    public int YStart { get; init; }

    /// <summary>Y 間距（÷100000 得 mm）。</summary>
    public uint PitchY { get; init; }

    /// <summary>Z 間距（÷100000 得 mm）。</summary>
    public uint PitchZ { get; init; }
}
