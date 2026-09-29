namespace ThreeDScan.Infrastructure.Repositories.Models;

/// <summary>對應資料表 ScannerProgramGroupName。</summary>
public sealed class ScannerProgramGroupNameEntity
{
    /// <summary>掃描器編號。</summary>
    public string ScannerId { get; set; } = "";

    /// <summary>Navigator 程式編號（0～15）。</summary>
    public byte ProgramNo { get; set; }

    /// <summary>群組顯示名稱。</summary>
    public string DisplayName { get; set; } = "";

    /// <summary>最後更新時刻。</summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>最後更新者。</summary>
    public string? UpdatedBy { get; set; }
}
