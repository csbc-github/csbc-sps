namespace ThreeDScan.Domain.Models;

/// <summary>C# <c>Pipeline.Run</c> 對單次擷取的匯出結果。</summary>
public sealed class SurfaceScanPipelineResult
{
    /// <summary>去地／去雜訊後點雲 PLY（公開絕對座標：相對 + cameraPosition + 補償）。</summary>
    public required string PointCloudPath { get; init; }

    /// <summary>STEP／glTF／PNG／PLY／JSON 打包 ZIP（公開絕對座標）。</summary>
    public required string ZipPath { get; init; }

    /// <summary>合併座標 JSON 絕對路徑（掃描器相對座標）。</summary>
    public string? JsonPath { get; init; }

    /// <summary>公開絕對座標 JSON；平移量為 0 時為 null。</summary>
    public string? CompensatedJsonPath { get; init; }

    /// <summary>自 ZIP 抽出的 GLB 絕對路徑。</summary>
    public string? GlbPath { get; init; }

    /// <summary>處理後點雲 Z 平均值（mm）。</summary>
    public double MeanZ { get; init; }

    /// <summary>本段前緣三點（公開絕對座標）；算不出時為 null。</summary>
    public HeadFeaturesRecord? HeadFeatures { get; init; }

    /// <summary>處理後點數。</summary>
    public int PointCount { get; init; }

    /// <summary>兩翼板夾角（度）；無法估算時為 null。</summary>
    public double? AngleDegrees { get; init; }

    /// <summary>偏離 90° 的角度（度）。</summary>
    public double? AngleDeviationFromSquareDegrees { get; init; }

    /// <summary>左側厚度（mm）；現場量測時有值。</summary>
    public double? ThicknessA { get; init; }

    /// <summary>右側厚度（mm）；現場量測時有值。</summary>
    public double? ThicknessB { get; init; }

    /// <summary>左側內表面最近 XY 點是否落在直方圖較低的那翼；未量測時為 null。</summary>
    public bool? ThicknessAOnLowFlange { get; init; }

    /// <summary>本次是否套用 UC-01 補償 offset。僅加 cameraPosition、沒有補償歷程時為 false。</summary>
    public bool CompensationApplied { get; init; }
}
