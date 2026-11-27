namespace ThreeDScan.Infrastructure.Export.Models;

/// <summary>孔洞分析結果。</summary>
/// <remarks>由 Hank Scan3D 的 HoleAnalysisResult 搬入，邏輯不改。</remarks>
public sealed class HoleAnalysisResult
{
    /// <summary>建立結果。</summary>
    /// <param name="featureBoundary">選取特徵邊界。</param>
    /// <param name="selectedFeatureId">選取特徵識別。</param>
    /// <param name="features">全部開口。</param>
    /// <param name="coordinateFrame">座標框名稱。</param>
    /// <param name="boundarySelection">邊界選取方式。</param>
    /// <param name="warnings">警告。</param>
    /// <param name="pointCount">點數。</param>
    /// <param name="scanPitchXMm">X 間距（mm）。</param>
    /// <param name="scanPitchYMm">Y 間距（mm）。</param>
    public HoleAnalysisResult(
        FeaturePoint[] featureBoundary,
        string? selectedFeatureId,
        FeatureOpening[] features,
        string coordinateFrame,
        string boundarySelection,
        string[] warnings,
        int pointCount,
        double scanPitchXMm,
        double scanPitchYMm)
    {
        FeatureBoundary = featureBoundary;
        SelectedFeatureId = selectedFeatureId;
        Features = features;
        CoordinateFrame = coordinateFrame;
        BoundarySelection = boundarySelection;
        Warnings = warnings;
        PointCount = pointCount;
        ScanPitchXMm = scanPitchXMm;
        ScanPitchYMm = scanPitchYMm;
    }

    /// <summary>選取特徵邊界。</summary>
    public FeaturePoint[] FeatureBoundary { get; init; }

    /// <summary>選取特徵識別。</summary>
    public string? SelectedFeatureId { get; init; }

    /// <summary>全部開口。</summary>
    public FeatureOpening[] Features { get; init; }

    /// <summary>座標框名稱。</summary>
    public string CoordinateFrame { get; init; }

    /// <summary>邊界選取方式。</summary>
    public string BoundarySelection { get; init; }

    /// <summary>警告。</summary>
    public string[] Warnings { get; init; }

    /// <summary>點數。</summary>
    public int PointCount { get; init; }

    /// <summary>X 間距（mm）。</summary>
    public double ScanPitchXMm { get; init; }

    /// <summary>Y 間距（mm）。</summary>
    public double ScanPitchYMm { get; init; }

    /// <summary>完整原生掃描器分析，保留鋼面、閉合判定、孔緣與掃描列端點。</summary>
    public HoleDetectionResult Detection { get; init; } = new();

    /// <summary>分析陣列索引對應到輸入 PLY 的頂點索引；ROI 不改變量測來源。</summary>
    public int[] AnalysisSourcePointIndices { get; init; } = [];

    /// <summary>ROI 過濾前，來源 PLY 的頂點數。</summary>
    public int SourcePointCount { get; init; }

    public int DetectedOpeningCount => Features.Length;
    public int ClosedHoleCount => Features.Count(f => f.Closed);
    public int PartialHoleCount => Features.Count(f => !f.Closed);

    /// <summary>角鋼板面篩選證據；null 表示直接分析完整來源點雲。</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public HoleScopeDiagnostics? Scope { get; init; }
}
