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
}
