namespace ThreeDScan.Domain.Models;

/// <summary>單段抽取選項。對應 Hank <c>Scan3D.Processing.SegmentProcessingOptions</c>。</summary>
public sealed class SegmentProcessingOptions
{
    /// <summary>選件策略。</summary>
    public ComponentSelection Selection { get; init; } = ComponentSelection.AngleSteel;

    /// <summary>指定候選 rank；null 依 Selection。</summary>
    public int? ComponentRank { get; init; }

    /// <summary>掃描器座標 ROI。</summary>
    public ScannerRoi? ScannerRoi { get; init; }

    /// <summary>物件分離用 Z 門檻（mm）。</summary>
    public double ZThresholdMm { get; init; } = 12;

    /// <summary>連通格邊長（mm）。</summary>
    public double CellMm { get; init; } = 3;

    /// <summary>最小連通格數。</summary>
    public int MinimumComponentCells { get; init; } = 50;

    /// <summary>翼板平面 RMS 上限（mm）。</summary>
    public double MaximumPlaneRmsMm { get; init; } = 0.5;

    /// <summary>厚度局部半徑（mm）。</summary>
    public double ThicknessLocalRadiusMm { get; init; } = 30;

    /// <summary>是否恢復同列外緣量測點。</summary>
    public bool RecoverMeasuredEdges { get; init; } = true;
}
