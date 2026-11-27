namespace ThreeDScan.Domain.Models;

/// <summary>拼接結果。對應 Hank <c>Scan3D.Processing.StitchProcessingResult</c>。</summary>
public sealed class StitchProcessingResult
{
    /// <summary>拼接 PLY。</summary>
    public string StitchedPlyPath { get; init; } = "";

    /// <summary>拼接報告。</summary>
    public string ReportPath { get; init; } = "";

    /// <summary>glTF 路徑。</summary>
    public string? GltfPath { get; init; }

    /// <summary>GLB 路徑。</summary>
    public string? GlbPath { get; init; }

    /// <summary>STEP 路徑。</summary>
    public string? StepPath { get; init; }

    /// <summary>模型報告。</summary>
    public string? ModelReportPath { get; init; }

    /// <summary>段數。</summary>
    public int SegmentCount { get; init; }

    /// <summary>點數。</summary>
    public int PointCount { get; init; }

    /// <summary>嚴格品質是否全部通過；與允許保留翼緣警告的點雲完成狀態分開。</summary>
    public bool ValidationPassed { get; init; }

    /// <summary>留出截面的對齊、翼板支承與資料互斥檢查；不適用或舊報告時為 null。</summary>
    public bool? AlignmentValidationPassed { get; init; }

    /// <summary>兩段翼緣及各端自身內側翼寬是否全部通過；不適用或舊報告時為 null。</summary>
    public bool? BoundaryValidationPassed { get; init; }

    /// <summary>未通過的接縫品質條件，含實際值及門檻。</summary>
    public StitchQualityFailure[] ValidationFailures { get; init; } = [];

    /// <summary>絕對座標是否已校正。</summary>
    public bool AbsoluteCoordinatesCalibrated { get; init; }

    /// <summary>採用的厚度（mm）。</summary>
    public double? ThicknessMm { get; init; }

    /// <summary>警告。</summary>
    public string[] Warnings { get; init; } = Array.Empty<string>();
}
