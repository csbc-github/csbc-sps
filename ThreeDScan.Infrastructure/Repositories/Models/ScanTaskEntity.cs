namespace ThreeDScan.Infrastructure.Repositories.Models;

/// <summary>對應資料表 ScanTask：一列存整份任務文件。</summary>
public sealed class ScanTaskEntity
{
    /// <summary>主鍵：任務編號。</summary>
    public string TaskId { get; set; } = "";

    /// <summary>種類（Surface／Hole／Line／Calibration）。</summary>
    public string Kind { get; set; } = "";

    /// <summary>狀態。</summary>
    public string Status { get; set; } = "";

    /// <summary>階段。</summary>
    public string Phase { get; set; } = "";

    /// <summary>掃描器編號。</summary>
    public string ScannerId { get; set; } = "";

    /// <summary>工單號。</summary>
    public string? JobId { get; set; }

    /// <summary>整份 TaskDocument JSON。</summary>
    public string DocumentJson { get; set; } = "";

    /// <summary>建立時刻。</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>最後更新時刻。</summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>回呼 URL。</summary>
    public string? CallbackUrl { get; set; }

    /// <summary>回呼是否已送達。</summary>
    public bool CallbackDelivered { get; set; }

    /// <summary>已嘗試回呼次數。</summary>
    public int CallbackAttempts { get; set; }

    /// <summary>最後一次回呼錯誤。</summary>
    public string? CallbackLastError { get; set; }

    /// <summary>下次回呼時刻。</summary>
    public DateTimeOffset? CallbackNextAttemptAt { get; set; }
}
