namespace ThreeDScan.Application.Scan.Models;

/// <summary>POST /api/scan/profile 請求。</summary>
public sealed class LineScanProfileRequest
{
    /// <summary>線掃描器編號。</summary>
    public string ScannerId { get; set; } = "";
}
