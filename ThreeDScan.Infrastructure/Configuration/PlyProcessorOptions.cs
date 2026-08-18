namespace ThreeDScan.Infrastructure.Configuration;

/// <summary>
/// 對齊 Hank code-V2_260716：Python backend（角鋼 PLY）+ C# Pipeline（ZIP/JSON/STEP/GLB/PNG/角度）。
/// </summary>
public sealed class PlyProcessorOptions
{
    public const string SectionName = "PlyProcessor";

    /// <summary>相對 <see cref="AppContext.BaseDirectory"/> 的 ply_processor 目錄。</summary>
    public string RootPath { get; set; } = "ply_processor";

    public int Port { get; set; } = 8000;

    // ── Python /process（等同 WinForms z_thresh / NoiseVoxel→cell）────────
    public float ZThresh { get; set; } = 12f;
    public float Cell { get; set; } = 3f;

    // ── C# Pipeline.RunProcessed（等同 WinForms PipelineOptions）──────────
    public float NoiseVoxel { get; set; } = 3f;
    public int MinNoiseSize { get; set; } = 200;
    public float VoxelSize { get; set; } = 10f;
    public int MinClusterSize { get; set; } = 200;
    public float PlaneTol { get; set; } = 8f;
    public bool RemoveDominantPlane { get; set; }
    public bool SinglePieceMode { get; set; }
    public float MeshCellSize { get; set; } = 2f;
    public float MeshThickness { get; set; } = 6f;
    public int SmoothIter { get; set; } = 4;
    public int ErodeN { get; set; } = 1;
    public int MeshBridgeFill { get; set; } = 30;

    public string ResolveRootPath()
    {
        var configured = string.IsNullOrWhiteSpace(RootPath) ? "ply_processor" : RootPath.Trim();
        if (Path.IsPathRooted(configured))
            return Path.GetFullPath(configured);
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, configured));
    }

    public PlyProcessor.Core.PipelineOptions ToPipelineOptions()
        => new(
            ZThresh: ZThresh,
            NoiseVoxel: NoiseVoxel > 0 ? NoiseVoxel : Cell,
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
