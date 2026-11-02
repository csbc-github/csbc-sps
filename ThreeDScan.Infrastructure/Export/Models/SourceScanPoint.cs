namespace ThreeDScan.Infrastructure.Export.Models;

/// <summary>原始掃描點（含來源索引與額外欄位）。</summary>
/// <remarks>由 Hank Scan3D 的 SourceScanPoint 搬入，邏輯不改。</remarks>
public readonly struct SourceScanPoint
{
    /// <summary>建立原始掃描點。</summary>
    /// <param name="sourcePointIndex">來源頂點索引。</param>
    /// <param name="position">掃描器座標。</param>
    /// <param name="extraColumns">x y z 之後的欄位字串。</param>
    public SourceScanPoint(int sourcePointIndex, ScanVector position, string extraColumns)
    {
        SourcePointIndex = sourcePointIndex;
        Position = position;
        ExtraColumns = extraColumns;
    }

    /// <summary>來源頂點索引。</summary>
    public int SourcePointIndex { get; }

    /// <summary>掃描器座標。</summary>
    public ScanVector Position { get; }

    /// <summary>x y z 之後的欄位字串。</summary>
    public string ExtraColumns { get; }
}
