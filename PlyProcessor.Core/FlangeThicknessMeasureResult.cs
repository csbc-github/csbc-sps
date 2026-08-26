namespace PlyProcessor.Core;

/// <summary>
/// 兩翼厚度量測結果（mm）。
/// 公式為「點雲上最近 XY 點的 Z − 夾具點 Z」，由噴印區 <c>measureThickness=true</c> 時計算。
/// </summary>
public sealed class FlangeThicknessMeasureResult
{
    /// <summary>
    /// 建立量測結果。
    /// </summary>
    /// <param name="thicknessA">翼 A 厚度（mm），對應夾具點 A。</param>
    /// <param name="thicknessB">翼 B 厚度（mm），對應夾具點 B。</param>
    public FlangeThicknessMeasureResult(double thicknessA, double thicknessB)
    {
        ThicknessA = thicknessA;
        ThicknessB = thicknessB;
    }

    /// <summary>翼 A 厚度（mm）：掃描點 Z 減夾具 A 的 Z。</summary>
    public double ThicknessA { get; }

    /// <summary>翼 B 厚度（mm）：掃描點 Z 減夾具 B 的 Z。</summary>
    public double ThicknessB { get; }
}
