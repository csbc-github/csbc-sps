namespace ThreeDScan.Infrastructure.Repositories.Models;

/// <summary>對應資料表 SystemConfig。</summary>
public sealed class SystemConfigEntity
{
    /// <summary>主鍵：設定鍵。</summary>
    public string ConfigKey { get; set; } = "";

    /// <summary>設定值。</summary>
    public string ConfigValue { get; set; } = "";

    /// <summary>最後更新時刻。</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}
