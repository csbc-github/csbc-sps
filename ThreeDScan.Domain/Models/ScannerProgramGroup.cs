namespace ThreeDScan.Domain.Models;

/// <summary>固定 16 組程式群組（ProgramNo 0～15），對應 Navigator Scan 1～16。</summary>
public sealed class ScannerProgramGroup
{
    public byte ProgramNo { get; init; }

    public required string DisplayName { get; set; }

    /// <summary>Navigator Scan 編號（1～16），API <c>navigatorScanNo</c> 使用此值。</summary>
    public int NavigatorScanNo => ProgramNo + 1;

    /// <summary>下拉選單顯示：項次 + 名稱。</summary>
    public string ListLabel => $"{NavigatorScanNo}. {DisplayName}";

    public override string ToString() => ListLabel;
}
