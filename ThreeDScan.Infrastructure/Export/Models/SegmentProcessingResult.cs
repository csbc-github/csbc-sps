using System.Text.Json.Serialization;
using ThreeDScan.Domain.Models;

namespace ThreeDScan.Infrastructure.Export.Models;

/// <summary>單段處理結果。對應 Hank <c>Scan3D.Processing.SegmentProcessingResult</c>。</summary>
public sealed class SegmentProcessingResult
{
    /// <summary>原始 PLY。</summary>
    public required string RawPlyPath { get; init; }

    /// <summary>掃描器座標角鋼 PLY。</summary>
    public required string ScannerPlyPath { get; init; }

    /// <summary>絕對座標角鋼 PLY。</summary>
    public required string AbsolutePlyPath { get; init; }

    /// <summary>處理報告 JSON。</summary>
    public required string ProcessingReportPath { get; init; }

    /// <summary>來源檔 SHA-256。</summary>
    public required string SourceSha256 { get; init; }

    /// <summary>該段相機位姿。</summary>
    public required Pose5 CameraPosition { get; init; }

    /// <summary>掃描器→絕對 4×4。</summary>
    public required double[][] ScannerToAbsoluteMatrix { get; init; }

    /// <summary>絕對座標是否已校正。</summary>
    public required bool AbsoluteCoordinatesCalibrated { get; init; }

    /// <summary>校正識別。</summary>
    public required string CalibrationId { get; init; }

    /// <summary>輸出點對應原始 PLY 索引。</summary>
    public required int[] SourcePointIndices { get; init; }

    /// <summary>候選物件。</summary>
    public required ComponentCandidate[] Candidates { get; init; }

    /// <summary>選用的 rank。</summary>
    public required int SelectedComponentRank { get; init; }

    /// <summary>警告。</summary>
    public required string[] Warnings { get; init; }

    /// <summary>幾何；無法擬合則 null。</summary>
    public SurfaceGeometry? Geometry { get; init; }

    /// <summary>幾何失敗原因。</summary>
    public string? GeometryError { get; init; }

    /// <summary>輸出點數。</summary>
    public int PointCount
    {
        get
        {
            return SourcePointIndices.Length;
        }
    }

    /// <summary>內部兩翼擬合（不序列化）。</summary>
    [JsonIgnore]
    internal WingFit? Fit { get; init; }
}
