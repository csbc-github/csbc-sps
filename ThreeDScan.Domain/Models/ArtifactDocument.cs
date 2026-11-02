namespace ThreeDScan.Domain.Models;

/// <summary>任務產物。對應 Hank <c>Scan3D.Service.ArtifactDocument</c>。</summary>
public sealed class ArtifactDocument
{
    /// <summary>檔案絕對路徑。</summary>
    public string Path { get; init; } = "";

    /// <summary>MIME 類型。</summary>
    public string ContentType { get; init; } = "";

    /// <summary>下載檔名。</summary>
    public string DownloadName { get; init; } = "";
}
