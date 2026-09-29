namespace ThreeDScan.Domain.Models;

/// <summary>儀器校正預覽影像在 ContentsRoot 下的相對路徑。</summary>
public sealed class InstrumentPreviewPaths
{
    /// <summary>灰階預覽相對路徑。</summary>
    public string? GrayRelativePath { get; init; }

    /// <summary>高度圖預覽相對路徑。</summary>
    public string? HeightRelativePath { get; init; }
}
