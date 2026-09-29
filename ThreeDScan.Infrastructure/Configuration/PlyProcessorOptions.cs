namespace ThreeDScan.Infrastructure.Configuration;

/// <summary>C# Pipeline 參數（對應 appsettings.json 的 PlyProcessor）。濾點現走 ScanProcessor.Process，匯出走 Pipeline.Export。</summary>
public sealed class PlyProcessorOptions
{
    /// <summary>設定區段名稱。</summary>
    public const string SectionName = "PlyProcessor";

    // 舊固定高度門檻（mm）。ScanProcessor 改自動空隙，已不讀、不傳入。
    // public float ZThresh { get; set; } = 12f;
    /// <summary>雜訊體素大小 (mm)。</summary>
    public float NoiseVoxel { get; set; } = 3f;

    /// <summary>雜訊叢集最小點數。</summary>
    public int MinNoiseSize { get; set; } = 200;

    /// <summary>分群體素大小 (mm)。</summary>
    public float VoxelSize { get; set; } = 10f;

    /// <summary>有效叢集最小點數。</summary>
    public int MinClusterSize { get; set; } = 200;

    /// <summary>主平面距離容差 (mm)。</summary>
    public float PlaneTol { get; set; } = 8f;

    /// <summary>是否移除主平面（地面）。</summary>
    public bool RemoveDominantPlane { get; set; }

    /// <summary>單件模式（不拆多件）。</summary>
    public bool SinglePieceMode { get; set; }

    /// <summary>網格單元大小 (mm)。</summary>
    public float MeshCellSize { get; set; } = 2f;

    /// <summary>網格厚度 (mm)。</summary>
    public float MeshThickness { get; set; } = 6f;

    /// <summary>平滑迭代次數。</summary>
    public int SmoothIter { get; set; } = 4;

    /// <summary>侵蝕次數。</summary>
    public int ErodeN { get; set; } = 1;

    /// <summary>橋接填補像素數。</summary>
    public int MeshBridgeFill { get; set; } = 30;

    /// <summary>多段拼接時段與段的 X 間距（mm）。</summary>
    public float StitchGap { get; set; }

    /// <summary>各段依 X 範圍裁兩端的百分比。</summary>
    public float StitchTrimPercent { get; set; } = 3f;

    /// <summary>後段 Y／Z 中位數對齊第一段。</summary>
    public bool StitchAlignYZ { get; set; } = true;

    /// <summary>拼接後全體最低 Z 歸零。</summary>
    public bool StitchZFloor { get; set; } = true;

    /// <summary>轉成多段拼接選項。</summary>
    public PlyProcessor.Core.ScanStitchOptions ToScanStitchOptions()
    {
        return new PlyProcessor.Core.ScanStitchOptions
        {
            GapMm = StitchGap,
            TrimPercent = StitchTrimPercent,
            AlignYZ = StitchAlignYZ,
            ZFloor = StitchZFloor,
        };
    }

    /// <summary>轉成 Pipeline 匯出選項。</summary>
    public PlyProcessor.Core.PipelineOptions ToPipelineOptions()
    {
        return new PlyProcessor.Core.PipelineOptions(
            NoiseVoxel: NoiseVoxel,
            MinNoiseSize: MinNoiseSize,
            VoxelSize: VoxelSize,
            MinClusterSize: MinClusterSize,
            PlaneTol: PlaneTol,
            RemoveDominantPlane: RemoveDominantPlane,
            SinglePieceMode: SinglePieceMode,
            MeshCellSize: MeshCellSize,
            MeshThickness: MeshThickness,
            SmoothIter: SmoothIter,
            ErodeN: ErodeN,
            MeshBridgeFill: MeshBridgeFill);
    }
}
