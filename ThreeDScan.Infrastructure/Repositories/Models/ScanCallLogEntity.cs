namespace ThreeDScan.Infrastructure.Repositories.Models;

/// <summary>對應資料表 <c>ScanCallLog</c>：掃描 API 舉證，與 <c>JobThickness</c> 分開。</summary>
public sealed class ScanCallLogEntity
{
    public long Id { get; set; }

    public DateTimeOffset Timestamp { get; set; }

    public string TraceId { get; set; } = "";

    public string Action { get; set; } = "";

    public string? ScannerId { get; set; }

    public string? JobId { get; set; }

    public string? TaskId { get; set; }

    public int? Seq { get; set; }

    public bool Success { get; set; }

    public string? ErrorCode { get; set; }

    public string? ErrorMessage { get; set; }

    public string? RequestJson { get; set; }

    public string? ResponseJson { get; set; }

    public string? PointCloudPath { get; set; }

    public string? PointCloudSha256 { get; set; }

    public string? ZipPath { get; set; }

    public string? ZipSha256 { get; set; }
}
