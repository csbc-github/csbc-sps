namespace ThreeDScan.Infrastructure.Repositories.Models;

/// <summary>對應資料表 ScannerProgramSettingsHistory。</summary>
public sealed class ScannerProgramSettingsHistoryEntity
{
    /// <summary>主鍵。</summary>
    public long Id { get; set; }

    /// <summary>掃描器編號。</summary>
    public string ScannerId { get; set; } = "";

    /// <summary>Navigator 程式編號（0～15）。</summary>
    public byte ProgramNo { get; set; }

    /// <summary>設定二進位內容。</summary>
    public byte[] SettingsBlob { get; set; } = Array.Empty<byte>();

    /// <summary>設定雜湊。</summary>
    public byte[] SettingsHash { get; set; } = Array.Empty<byte>();

    /// <summary>存檔時刻。</summary>
    public DateTimeOffset SavedAt { get; set; }

    /// <summary>最後更新者。</summary>
    public string? UpdatedBy { get; set; }

    /// <summary>追溯 ID。</summary>
    public string TraceId { get; set; } = "";

    /// <summary>寫入來源。</summary>
    public string Source { get; set; } = "";
}
