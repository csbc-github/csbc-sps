namespace ThreeDScan.Domain.Models;

/// <summary>儀器校正預覽影像在 ContentsRoot 下的相對路徑。</summary>
public sealed class InstrumentPreviewPaths
{
    public string? GrayRelativePath { get; init; }
    public string? HeightRelativePath { get; init; }
}
