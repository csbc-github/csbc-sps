namespace ThreeDScan.Application.Scan.Models;

/// <summary>架設預覽開始／結束請求。</summary>
public sealed class LineScanSetupRequest
{
    /// <summary>線掃描器編號。</summary>
    public string ScannerId { get; set; } = "";
}
