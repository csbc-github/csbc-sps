namespace ThreeDScan.Infrastructure.Export.Models;

/// <summary>依原模型外框補平外緣的推估幾何紀錄；不代表新增量測點。</summary>
public sealed class OutlineFillReport
{
    /// <summary>是否完成逐實體的外緣補平。</summary>
    public bool Applied { get; init; }
    /// <summary>無法安全分別補平時保留原模型的原因。</summary>
    public string? SkipReason { get; init; }
    /// <summary>新增外框材料屬推估幾何。</summary>
    public bool EstimatedGeometry => true;
    /// <summary>量測點雲不經此程序修改。</summary>
    public bool OriginalMeasuredPointCloudUnchanged => true;
    /// <summary>補平方法。</summary>
    public string Method => "Evaluate original row/column outer-edge gaps against each solid's fixed UV envelope; fill only scanline notches deeper than the configured threshold. Preserve original enclosed holes, fill exterior pockets closed by that repair, and keep separate major solids separate.";
    /// <summary>僅修補深度嚴格大於此值的外緣缺口；0 表示整框補平。</summary>
    public double MinimumNotchDepthMm { get; init; }
    /// <summary>來源實體數。</summary>
    public int SourceSolidCount { get; init; }
    /// <summary>輸出實體數。</summary>
    public int FinalSolidCount { get; init; }
    /// <summary>來源封閉內孔數。</summary>
    public int SourceThroughHoleCount { get; init; }
    /// <summary>輸出封閉內孔數。</summary>
    public int FinalThroughHoleCount { get; init; }
    /// <summary>排除的微小邊緣孤島數；只影響模型。</summary>
    public int ExcludedSmallIslandCount { get; init; }
    /// <summary>排除孤島體積（mm³）。</summary>
    public double ExcludedSmallIslandVolumeMm3 { get; init; }
    /// <summary>原始保留實體佔用格數。</summary>
    public int OriginalOccupiedCells { get; init; }
    /// <summary>補入的外部連通缺格數。</summary>
    public int FilledExteriorCells { get; init; }
    /// <summary>大缺口修補後封住的原外部空白格；填入以避免形成新增內孔。</summary>
    public int FilledEnclosedExteriorPocketCells { get; init; }
    /// <summary>保留的封閉內孔格數。</summary>
    public int RetainedEnclosedHoleCells { get; init; }
    /// <summary>補入的投影面積（mm²）。</summary>
    public double AddedProjectedAreaMm2 { get; init; }
    /// <summary>補入的推估體積（mm³），未扣除排除孤島。</summary>
    public double AddedVolumeMm3 { get; init; }
    /// <summary>各保留實體在來源重建框架內的格數與範圍。</summary>
    public OutlineFillBodyReport[] Bodies { get; init; } = [];
}

/// <summary>單一實體的補平外框；UV 尺寸在來源重建框架內表示。</summary>
public sealed class OutlineFillBodyReport
{
    public int SourceSolid { get; init; }
    public int ReconstructionIndex { get; init; }
    public double MinCrossMm { get; init; }
    public double MaxCrossMm { get; init; }
    public double MinAlongMm { get; init; }
    public double MaxAlongMm { get; init; }
    public int OriginalOccupiedCells { get; init; }
    public int FilledExteriorCells { get; init; }
    public int FilledEnclosedExteriorPocketCells { get; init; }
    public int RetainedEnclosedHoleCells { get; init; }
    public double AddedProjectedAreaMm2 { get; init; }
    public double AddedVolumeMm3 { get; init; }
}
