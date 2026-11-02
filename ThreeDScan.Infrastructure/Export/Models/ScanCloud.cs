namespace ThreeDScan.Infrastructure.Export.Models;

/// <summary>掃描點雲與 PLY 標頭。</summary>
/// <remarks>由 Hank Scan3D 的 ScanCloud 搬入，邏輯不改。</remarks>
public sealed class ScanCloud
{
    /// <summary>建立空點雲。</summary>
    public ScanCloud()
    {
        Header = Array.Empty<string>();
        Points = Array.Empty<SourceScanPoint>();
    }

    /// <summary>建立點雲。</summary>
    /// <param name="header">PLY 標頭列。</param>
    /// <param name="points">頂點。</param>
    /// <param name="sourceSha256">來源檔 SHA-256；可為 null。</param>
    public ScanCloud(string[] header, SourceScanPoint[] points, string? sourceSha256 = null)
    {
        Header = header;
        Points = points;
        SourceSha256 = sourceSha256;
    }

    /// <summary>PLY 標頭列。</summary>
    public string[] Header { get; init; }

    /// <summary>頂點。</summary>
    public SourceScanPoint[] Points { get; init; }

    /// <summary>來源檔 SHA-256。</summary>
    public string? SourceSha256 { get; init; }
}
