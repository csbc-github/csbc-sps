namespace ThreeDScan.Application.Scan.Models;

/// <summary>面掃描附加 ZIP 的本機路徑（Controller 以檔案下載，不進 JSON）。</summary>
public sealed class SurfaceScanAttachmentFile
{
    public string ZipFullPath { get; init; } = "";

    public string DownloadFileName { get; init; } = "";
}
