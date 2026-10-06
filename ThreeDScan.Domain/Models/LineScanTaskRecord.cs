namespace ThreeDScan.Domain.Models;

/// <summary>進行中的線掃描任務（檔案暫存，含錄製目錄與滾輪速度）。</summary>
public sealed class LineScanTaskRecord
{
    /// <summary>任務識別碼。</summary>
    public string TaskId { get; set; } = "";

    /// <summary>光達感測器編號。</summary>
    public string ScannerId { get; set; } = "";

    /// <summary>滾輪速度 (mm/s)。掃描過程中視為恆定。</summary>
    public double RollerSpeed { get; set; }

    /// <summary>開始當下的追溯 ID。</summary>
    public string TraceId { get; set; } = "";

    /// <summary>截面錄製目錄。</summary>
    public string SessionDirectory { get; set; } = "";

    /// <summary>已接受 complete，背景正在整理結果。</summary>
    public bool Completing { get; set; }

    /// <summary>已中止。後續 complete 視同任務不存在。</summary>
    public bool Aborted { get; set; }

    /// <summary>已結束（成功或中止），Watchdog 不再處理。</summary>
    public bool IsCompleted { get; set; }

    /// <summary>任務建立時間。</summary>
    public DateTimeOffset StartedAt { get; set; }

    /// <summary>最後更新時間。</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}
