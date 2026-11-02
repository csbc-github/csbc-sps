namespace ThreeDScan.Infrastructure.Export.Models;

/// <summary>
/// SavePly 結果。對應 Hank <c>Scan3D.Scanner.ExportResult</c>。
/// </summary>
public sealed class PlyExportResult
{
    /// <summary>寫出的絕對路徑。</summary>
    public required string Path { get; init; }

    /// <summary>有效頂點數。</summary>
    public int VertexCount { get; init; }

    /// <summary>檔案位元組數。</summary>
    public long FileBytes { get; init; }
}
