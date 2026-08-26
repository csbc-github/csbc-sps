namespace ThreeDScan.Domain.Models;

/// <summary>面掃描任務狀態（檔案暫存，供分段 seq 與附件下載）。</summary>
public sealed class SurfaceScanTaskRecord
{
    public string TaskId { get; set; } = "";

    public string ScannerId { get; set; } = "";

    /// <summary>已完成的呼叫次數；建立時為 0。</summary>
    public int Seq { get; set; }

    public string? ZipPath { get; set; }

    public string? PointCloudPath { get; set; }

    public double? LastMeanZ { get; set; }

    /// <summary>上次呼叫 <c>final=true</c> 後不可再掃。</summary>
    public bool IsCompleted { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
