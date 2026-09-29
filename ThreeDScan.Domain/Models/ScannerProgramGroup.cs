namespace ThreeDScan.Domain.Models;

/// <summary>固定 16 組程式群組（ProgramNo 0～15），對應 Navigator Scan 1～16。</summary>
public sealed class ScannerProgramGroup
{
    /// <summary>Navigator 程式編號（0～15）。</summary>
    public byte ProgramNo { get; init; }

    /// <summary>群組顯示名稱。</summary>
    public required string DisplayName { get; set; }

    /// <summary>Navigator Scan 編號（1～16），API <c>navigatorScanNo</c> 使用此值。</summary>
    public int NavigatorScanNo
    {
        get
        {
            return ProgramNo + 1;
        }
    }

    /// <summary>下拉選單顯示：項次 + 名稱。</summary>
    public string ListLabel
    {
        get
        {
            return $"{NavigatorScanNo}. {DisplayName}";
        }
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return ListLabel;
    }
}
