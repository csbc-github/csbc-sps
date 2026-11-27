namespace ThreeDScan.Domain.Models;

/// <summary>拼接選項。對應 Hank <c>Scan3D.Processing.StitchProcessingOptions</c>。</summary>
public sealed class StitchProcessingOptions
{
    /// <summary>拼接模式。</summary>
    public StitchMode Mode { get; init; } = StitchMode.CameraPose;

    /// <summary>公稱對接間隙（mm）。</summary>
    public double NominalGapMm { get; init; }

    /// <summary>網格邊長（mm）。</summary>
    public double MeshCellMm { get; init; } = 1;

    /// <summary>是否匯出實體模型。</summary>
    public bool ExportModels { get; init; } = true;

    /// <summary>修補超過門檻的模型外緣缺口；保留內孔與量測點雲，補入部分標記為推估幾何。</summary>
    public bool FillOuterEdges { get; init; } = true;

    /// <summary>外緣缺口深度超過此值才修補（mm）；0 為補至完整直線外框。</summary>
    public double OuterEdgeRepairMinDepthMm { get; init; } = 10;

    /// <summary>另存各段對齊與完整轉換點雲；API 預設只寫正式合併點雲及完整接縫報告。</summary>
    public bool ExportDiagnosticPointClouds { get; init; }
}
