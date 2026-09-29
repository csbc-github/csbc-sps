namespace ThreeDScan.Application.Scan.Models;

/// <summary>面掃描 complete 最終 result（callback／GET 查詢）。</summary>
public sealed class SurfaceScanCompleteResult
{
    /// <summary>拼接後之完整點雲下載位置。</summary>
    public string PointCloudUrl { get; init; } = "";
}
