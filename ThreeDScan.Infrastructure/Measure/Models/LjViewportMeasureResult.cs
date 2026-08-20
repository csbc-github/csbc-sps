using Keyence.LjDevCommon;
using Keyence.LjDevMeasure;

namespace ThreeDScan.Infrastructure.Measure.Models;

public sealed class LjViewportMeasureResult
{
    public required LHeightImage Height { get; init; }
    public required LGrayImage Gray { get; init; }
    public required IList<ILToolInfo> ToolResults { get; init; }
}
