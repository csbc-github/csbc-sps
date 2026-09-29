using Keyence.LjDevCommon;
using Keyence.LjDevMeasure;

namespace ThreeDScan.Infrastructure.Measure.Models;

/// <summary>視埠量測用的高度圖、灰階與工具結果。</summary>
public sealed class LjViewportMeasureResult
{
    /// <summary>高度圖。</summary>
    public required LHeightImage Height { get; init; }

    /// <summary>灰階影像。</summary>
    public required LGrayImage Gray { get; init; }

    /// <summary>SDK 工具量測結果。</summary>
    public required IList<ILToolInfo> ToolResults { get; init; }
}
