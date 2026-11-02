namespace ThreeDScan.Infrastructure.Export.Models;

/// <summary>實體拓樸檢查。</summary>
/// <remarks>由 Hank Scan3D 的 SolidTopology 搬入，邏輯不改。</remarks>
public sealed class SolidTopology
{
    /// <summary>建立拓樸。</summary>
    /// <param name="vertices">頂點數。</param>
    /// <param name="triangles">三角形數。</param>
    /// <param name="edges">邊數。</param>
    /// <param name="closedSolidCount">封閉實體數。</param>
    /// <param name="throughHoleCount">貫穿孔數。</param>
    /// <param name="volumeMm3">體積（mm³）。</param>
    /// <param name="closedAndOriented">是否封閉且方向一致。</param>
    public SolidTopology(
        int vertices,
        int triangles,
        int edges,
        int closedSolidCount,
        int throughHoleCount,
        double volumeMm3,
        bool closedAndOriented)
    {
        Vertices = vertices;
        Triangles = triangles;
        Edges = edges;
        ClosedSolidCount = closedSolidCount;
        ThroughHoleCount = throughHoleCount;
        VolumeMm3 = volumeMm3;
        ClosedAndOriented = closedAndOriented;
    }

    /// <summary>頂點數。</summary>
    public int Vertices { get; init; }

    /// <summary>三角形數。</summary>
    public int Triangles { get; init; }

    /// <summary>邊數。</summary>
    public int Edges { get; init; }

    /// <summary>封閉實體數。</summary>
    public int ClosedSolidCount { get; init; }

    /// <summary>貫穿孔數。</summary>
    public int ThroughHoleCount { get; init; }

    /// <summary>體積（mm³）。</summary>
    public double VolumeMm3 { get; init; }

    /// <summary>是否封閉且方向一致。</summary>
    public bool ClosedAndOriented { get; init; }
}
