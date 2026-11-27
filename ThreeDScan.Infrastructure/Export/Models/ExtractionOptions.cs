namespace ThreeDScan.Infrastructure.Export.Models;

/// <summary>角鋼抽取參數。</summary>
/// <remarks>高度門檻建立種子；地面估計與實測低邊緣恢復另有獨立品質限制。</remarks>
public sealed class ExtractionOptions
{
    /// <summary>使用預設參數。</summary>
    public ExtractionOptions()
    {
    }

    /// <summary>指定高度門檻、格子與最小連通格數。</summary>
    /// <param name="zThresholdMm">Z 門檻（mm）。</param>
    /// <param name="cellMm">連通格邊長（mm）。</param>
    /// <param name="minimumComponentCells">最小連通格數。</param>
    public ExtractionOptions(double zThresholdMm, double cellMm, int minimumComponentCells)
    {
        ZThresholdMm = zThresholdMm;
        CellMm = cellMm;
        MinimumComponentCells = minimumComponentCells;
    }

    /// <summary>物件連通種子用 Z 門檻（mm），不直接裁掉符合板面與連續性的實測低邊緣。</summary>
    public double ZThresholdMm { get; init; } = 12;

    /// <summary>連通格邊長（mm）。</summary>
    public double CellMm { get; init; } = 3;

    /// <summary>最小連通格數。</summary>
    public int MinimumComponentCells { get; init; } = 50;

    /// <summary>舊版四角視窗（mm）；僅保留設定相容，新分區地面估計不使用。</summary>
    public double CornerWindowMm { get; init; } = 12;

    /// <summary>舊版四角取樣步距；僅保留設定相容，新分區地面估計不使用。</summary>
    public int CornerSample { get; init; } = 10;

    /// <summary>地面估計最多取樣點數；擬合成本不隨完整點雲線性放大。</summary>
    public int GroundMaxSamples { get; init; } = 60_000;

    /// <summary>地面平面的正交支持距離（mm）。</summary>
    public double GroundDistanceToleranceMm { get; init; } = .75;

    /// <summary>允許的地面斜率 sqrt(A²+B²)。</summary>
    public double GroundMaximumSlope { get; init; } = 1;

    /// <summary>地面最低樣本／區塊支持比例。</summary>
    public double GroundMinimumSupportFraction { get; init; } = .12;

    /// <summary>高度門檻只建立種子，再恢復同板面的連續實測低邊緣。</summary>
    public bool RecoverMeasuredEdges { get; init; } = true;
}
