namespace ThreeDScan.Domain.Models;

/// <summary>C# <c>Pipeline.Run</c> 對單次擷取的匯出結果。</summary>
public sealed class SurfaceScanPipelineResult
{
    /// <summary>去地／去雜訊後點雲 PLY 絕對路徑。</summary>
    public required string PointCloudPath { get; init; }

    /// <summary>STEP／glTF／PNG／PLY／JSON 打包 ZIP 絕對路徑。</summary>
    public required string ZipPath { get; init; }

    /// <summary>合併座標 JSON 絕對路徑。</summary>
    public string? JsonPath { get; init; }

    /// <summary>自 ZIP 抽出的 GLB 絕對路徑。</summary>
    public string? GlbPath { get; init; }

    /// <summary>處理後點雲 Z 平均值（mm），供分段斜率差。</summary>
    public double MeanZ { get; init; }

    /// <summary>處理後點數。</summary>
    public int PointCount { get; init; }

    /// <summary>兩翼板夾角（度）；無法估算時為 null。</summary>
    public double? AngleDegrees { get; init; }

    /// <summary>偏離 90° 的角度（度）。</summary>
    public double? AngleDeviationFromSquareDegrees { get; init; }
}
