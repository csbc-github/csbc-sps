using System.Text.Json.Serialization;

namespace ThreeDScan.Infrastructure.Export.Models;

/// <summary>角鋼抽取結果。</summary>
/// <remarks>由 Hank Scan3D 的 ExtractionResult 搬入，邏輯不改。</remarks>
public sealed class ExtractionResult
{
    /// <summary>來源點雲（不序列化）。</summary>
    [JsonIgnore]
    public required ScanCloud Source { get; init; }

    /// <summary>高度以上的校正點（不序列化）。</summary>
    [JsonIgnore]
    public required CalibratedScanPoint[] AbovePoints { get; init; }

    /// <summary>抽取參數。</summary>
    public required ExtractionOptions Options { get; init; }

    /// <summary>地面／邊緣校正。</summary>
    public required ScanCalibration Calibration { get; init; }

    /// <summary>全部連通物件。</summary>
    public required ExtractionComponent[] Components { get; init; }

    /// <summary>來源點數。</summary>
    public int SourcePointCount
    {
        get
        {
            return Source.Points.Length;
        }
    }

    /// <summary>高度以上點數。</summary>
    public int AboveThresholdCount
    {
        get
        {
            return AbovePoints.Length;
        }
    }

    /// <summary>佔用格數。</summary>
    public int OccupiedCellCount { get; init; }

    /// <summary>單位。</summary>
    public string Unit
    {
        get
        {
            return "mm";
        }
    }

    /// <summary>警告。</summary>
    public string[] Warnings { get; init; } = [];

    /// <summary>達門檻的物件（不序列化）。</summary>
    [JsonIgnore]
    public ExtractionComponent[] Objects
    {
        get
        {
            return Components.Where(c => c.Retained).ToArray();
        }
    }

    /// <summary>名稱為 angle_steel 的物件（不序列化）。</summary>
    [JsonIgnore]
    public ExtractionComponent? AngleSteel
    {
        get
        {
            return Objects.FirstOrDefault(c => c.Name == "angle_steel");
        }
    }
}
