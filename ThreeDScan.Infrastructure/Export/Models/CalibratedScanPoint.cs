namespace ThreeDScan.Infrastructure.Export.Models;

/// <summary>經地面校正後的點。</summary>
/// <remarks>由 Hank Scan3D 的 CalibratedScanPoint 搬入，邏輯不改。</remarks>
public readonly struct CalibratedScanPoint
{
    /// <summary>建立校正點。</summary>
    /// <param name="sourcePointIndex">來源頂點索引。</param>
    /// <param name="position">校正後座標。</param>
    /// <param name="extraColumns">額外欄位字串。</param>
    public CalibratedScanPoint(int sourcePointIndex, ScanVector position, string extraColumns)
    {
        SourcePointIndex = sourcePointIndex;
        Position = position;
        ExtraColumns = extraColumns;
    }

    /// <summary>來源頂點索引。</summary>
    public int SourcePointIndex { get; }

    /// <summary>校正後座標。</summary>
    public ScanVector Position { get; }

    /// <summary>額外欄位字串。</summary>
    public string ExtraColumns { get; }
}
