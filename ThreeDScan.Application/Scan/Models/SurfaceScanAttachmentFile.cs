namespace ThreeDScan.Application.Scan.Models;

/// <summary>面掃描附加 ZIP 的本機路徑（Controller 以檔案下載，不進 JSON）。</summary>
public sealed class SurfaceScanAttachmentFile
{
    /// <summary>ZIP 本機絕對路徑。</summary>
    public string ZipFullPath { get; init; } = "";

    /// <summary>下載時的檔名。</summary>
    public string DownloadFileName { get; init; } = "";
}
