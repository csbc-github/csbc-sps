namespace ThreeDScan.Domain.Models;

/// <summary>面掃描任務狀態（檔案暫存，供分段 seq 與附件下載）。</summary>
public sealed class SurfaceScanTaskRecord
{
    /// <summary>任務識別碼。</summary>
    public string TaskId { get; set; } = "";

    /// <summary>掃描器編號。</summary>
    public string ScannerId { get; set; } = "";

    /// <summary>工單號（start 寫入，segment／complete 沿用）。</summary>
    public string JobId { get; set; } = "";

    /// <summary>已完成的呼叫次數；建立時為 0。</summary>
    public int Seq { get; set; }

    /// <summary>最後一段 ZIP 路徑。</summary>
    public string? ZipPath { get; set; }

    /// <summary>最後一段點雲路徑。</summary>
    public string? PointCloudPath { get; set; }

    /// <summary>各段公開點雲 PLY 絕對路徑，順序即拼接順序。</summary>
    public List<string> SegmentPlyPaths { get; set; } = new();

    /// <summary>上一段點雲平均 Z (mm)，供段間高度差。</summary>
    public double? LastMeanZ { get; set; }

    /// <summary>上次呼叫 <c>final=true</c> 後不可再掃。</summary>
    public bool IsCompleted { get; set; }

    /// <summary>最後更新時刻。</summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>任務建立時間（Watchdog 自此時起算）。</summary>
    public DateTimeOffset StartedAt { get; set; }
}
