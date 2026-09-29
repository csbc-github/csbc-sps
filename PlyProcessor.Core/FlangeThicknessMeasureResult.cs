namespace PlyProcessor.Core;

/// <summary>
/// 兩斜面厚度量測結果（mm）。厚度為外表面局部平面到內表面測距點的投影距離。
/// </summary>
public sealed class FlangeThicknessMeasureResult
{
    /// <summary>
    /// 建立量測結果。
    /// </summary>
    /// <param name="thicknessLeft">左側厚度（mm）；未量測或無法計算時為 null。</param>
    /// <param name="thicknessRight">右側厚度（mm）；未量測或無法計算時為 null。</param>
    public FlangeThicknessMeasureResult(double? thicknessLeft, double? thicknessRight)
    {
        ThicknessLeft = thicknessLeft;
        ThicknessRight = thicknessRight;
    }

    /// <summary>左側厚度（mm）。</summary>
    public double? ThicknessLeft { get; }

    /// <summary>右側厚度（mm）。</summary>
    public double? ThicknessRight { get; }
}
