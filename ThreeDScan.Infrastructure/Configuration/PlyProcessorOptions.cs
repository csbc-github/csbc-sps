namespace ThreeDScan.Infrastructure.Configuration;

/// <summary>C# <c>Pipeline.Run</c> 參數（對應 <c>appsettings.json</c> 的 PlyProcessor）。</summary>
public sealed class PlyProcessorOptions
{
    public const string SectionName = "PlyProcessor";

    public float ZThresh { get; set; } = 12f;
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

    public PlyProcessor.Core.PipelineOptions ToPipelineOptions()
    {
        return new PlyProcessor.Core.PipelineOptions(
            ZThresh: ZThresh,
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
