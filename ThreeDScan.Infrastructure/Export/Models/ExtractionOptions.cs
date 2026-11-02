namespace ThreeDScan.Infrastructure.Export.Models;

/// <summary>角鋼抽取參數。</summary>
/// <remarks>由 Hank Scan3D 的 ExtractionOptions 搬入，邏輯不改。</remarks>
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

    /// <summary>物件分離用 Z 門檻（mm）。</summary>
    public double ZThresholdMm { get; init; } = 12;

    /// <summary>連通格邊長（mm）。</summary>
    public double CellMm { get; init; } = 3;

    /// <summary>最小連通格數。</summary>
    public int MinimumComponentCells { get; init; } = 50;

    /// <summary>四角地面視窗（mm）。</summary>
    public double CornerWindowMm { get; init; } = 12;

    /// <summary>四角取樣步距。</summary>
    public int CornerSample { get; init; } = 10;
}
