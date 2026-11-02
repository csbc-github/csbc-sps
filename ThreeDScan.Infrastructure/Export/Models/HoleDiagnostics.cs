namespace ThreeDScan.Infrastructure.Export.Models;

/// <summary>單面孔洞偵測診斷。</summary>
/// <remarks>由 Hank Scan3D 的 HoleDiagnostics 搬入，邏輯不改。</remarks>
public sealed class HoleDiagnostics
{
    /// <summary>建立診斷。</summary>
    /// <param name="faceId">面識別。</param>
    /// <param name="assignedPointCount">歸屬點數。</param>
    /// <param name="gapEvents">缺口事件數。</param>
    /// <param name="components">連通數。</param>
    /// <param name="rejectedExterior">因無封口拒絕數。</param>
    /// <param name="rejectedSmall">因太小拒絕數。</param>
    /// <param name="rejectedSurfaceCutoffs">因表面回波拒絕數。</param>
    public HoleDiagnostics(
        string faceId,
        int assignedPointCount,
        int gapEvents,
        int components,
        int rejectedExterior,
        int rejectedSmall,
        int rejectedSurfaceCutoffs)
    {
        FaceId = faceId;
        AssignedPointCount = assignedPointCount;
        GapEvents = gapEvents;
        Components = components;
        RejectedExterior = rejectedExterior;
        RejectedSmall = rejectedSmall;
        RejectedSurfaceCutoffs = rejectedSurfaceCutoffs;
    }

    /// <summary>面識別。</summary>
    public string FaceId { get; init; }

    /// <summary>歸屬點數。</summary>
    public int AssignedPointCount { get; init; }

    /// <summary>缺口事件數。</summary>
    public int GapEvents { get; init; }

    /// <summary>連通數。</summary>
    public int Components { get; init; }

    /// <summary>因無封口拒絕數。</summary>
    public int RejectedExterior { get; init; }

    /// <summary>因太小拒絕數。</summary>
    public int RejectedSmall { get; init; }

    /// <summary>因表面回波拒絕數。</summary>
    public int RejectedSurfaceCutoffs { get; init; }
}
