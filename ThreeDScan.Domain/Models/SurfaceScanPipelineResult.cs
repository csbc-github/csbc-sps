namespace ThreeDScan.Domain.Models;

/// <summary>C# <c>Pipeline.Run</c> 對單次擷取的匯出結果。</summary>
public sealed class SurfaceScanPipelineResult
{
    /// <summary>去地／去雜訊後點雲 PLY（有補償歷程時為已套 offset）。</summary>
    public required string PointCloudPath { get; init; }

    /// <summary>STEP／glTF／PNG／PLY／JSON 打包 ZIP（有補償歷程時為已套 offset）。</summary>
    public required string ZipPath { get; init; }

    /// <summary>合併座標 JSON 絕對路徑（未套補償）。</summary>
    public string? JsonPath { get; init; }

    /// <summary>套上最新儀器校正 offset 的座標 JSON；無補償歷程時為 null。</summary>
    public string? CompensatedJsonPath { get; init; }

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

    /// <summary>翼 A 厚度（mm）；僅噴印現場量測時有值，割切沿用時為 null。</summary>
    public double? ThicknessA { get; init; }

    /// <summary>翼 B 厚度（mm）；僅噴印現場量測時有值。</summary>
    public double? ThicknessB { get; init; }

    /// <summary>
    /// 噴印量測時，夾具 A 是否落在直方圖較低的那翼。
    /// 寫入 JobThickness 後，割切組網格沿用此對應；未量測時為 null。
    /// </summary>
    public bool? ThicknessAOnLowFlange { get; init; }
}
