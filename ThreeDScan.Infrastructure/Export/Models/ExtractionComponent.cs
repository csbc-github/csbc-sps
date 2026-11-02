namespace ThreeDScan.Infrastructure.Export.Models;

/// <summary>抽取到的連通物件。</summary>
/// <remarks>由 Hank Scan3D 的 ExtractionComponent 搬入，邏輯不改。</remarks>
public sealed class ExtractionComponent
{
    /// <summary>建立連通物件。</summary>
    /// <param name="label">內部標籤。</param>
    /// <param name="rank">由大到小的排名。</param>
    /// <param name="cellCount">連通格數。</param>
    /// <param name="pointCount">點數。</param>
    /// <param name="retained">是否達最小格數。</param>
    /// <param name="name">物件名稱。</param>
    /// <param name="abovePointIndices">高度過濾後點索引。</param>
    /// <param name="bounds">包圍盒。</param>
    public ExtractionComponent(
        int label,
        int rank,
        int cellCount,
        int pointCount,
        bool retained,
        string name,
        int[] abovePointIndices,
        ScanBounds bounds)
    {
        Label = label;
        Rank = rank;
        CellCount = cellCount;
        PointCount = pointCount;
        Retained = retained;
        Name = name;
        AbovePointIndices = abovePointIndices;
        Bounds = bounds;
    }

    /// <summary>內部標籤。</summary>
    public int Label { get; init; }

    /// <summary>由大到小的排名。</summary>
    public int Rank { get; init; }

    /// <summary>連通格數。</summary>
    public int CellCount { get; init; }

    /// <summary>點數。</summary>
    public int PointCount { get; init; }

    /// <summary>是否達最小格數。</summary>
    public bool Retained { get; init; }

    /// <summary>物件名稱。</summary>
    public string Name { get; init; }

    /// <summary>高度過濾後點索引。</summary>
    public int[] AbovePointIndices { get; init; }

    /// <summary>包圍盒。</summary>
    public ScanBounds Bounds { get; init; }
}
