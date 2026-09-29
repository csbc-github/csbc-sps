namespace ThreeDScan.Infrastructure.Repositories.Models;

/// <summary>對應資料表 ScannerProgramSettings（目前生效的拍攝參數）。</summary>
public sealed class ScannerProgramSettingsEntity
{
    /// <summary>掃描器編號。</summary>
    public string ScannerId { get; set; } = "";

    /// <summary>Navigator 程式編號（0～15）。</summary>
    public byte ProgramNo { get; set; }

    /// <summary>設定二進位內容。</summary>
    public byte[] SettingsBlob { get; set; } = Array.Empty<byte>();

    /// <summary>設定雜湊。</summary>
    public byte[] SettingsHash { get; set; } = Array.Empty<byte>();

    /// <summary>最後更新時刻。</summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>最後更新者。</summary>
    public string? UpdatedBy { get; set; }

    /// <summary>追溯 ID。</summary>
    public string TraceId { get; set; } = "";

    /// <summary>寫入來源。</summary>
    public string Source { get; set; } = "";
}
