namespace ThreeDScan.Application.Scan.Models;

/// <summary>面掃描 complete 最終 result（callback／GET 查詢）。</summary>
public sealed class SurfaceScanCompleteResult
{
    /// <summary>拼接後之完整點雲下載位置。</summary>
    public string PointCloudUrl { get; init; } = "";

    /// <summary>規格 v1.1 未列，由 Hank 搬入。附件是否可下載。</summary>
    public bool AttachmentsAvailable { get; init; }

    /// <summary>規格 v1.1 未列，由 Hank 搬入。附件 URL。</summary>
    public string? AttachmentsUrl { get; init; }

    /// <summary>規格 v1.1 未列，由 Hank 搬入。附件不可用原因。</summary>
    public string? AttachmentsUnavailableReason { get; init; }

    /// <summary>規格 v1.1 未列，由 Hank 搬入。段數。</summary>
    public int SegmentCount { get; init; }

    /// <summary>實際採用的拼接模式名稱（NominalButtJoin／CameraPose）。舊結果可能未提供。</summary>
    public string? StitchMode { get; init; }

    /// <summary>首尾對接所採用的公稱間隙（mm）。</summary>
    public double? NominalGapMm { get; init; }

    /// <summary>規格 v1.1 未列，由 Hank 搬入。點數。</summary>
    public int PointCount { get; init; }

    /// <summary>規格 v1.1 未列，由 Hank 搬入。是否已現場校正。</summary>
    public bool AbsoluteCoordinatesCalibrated { get; init; }

    /// <summary>規格 v1.1 未列，由 Hank 搬入。板厚（mm）。</summary>
    public double? ThicknessMm { get; init; }

    /// <summary>規格 v1.1 未列，由 Hank 搬入。警告。</summary>
    public IReadOnlyList<string>? Warnings { get; init; }

    /// <summary>嚴格品質檢查結果；點雲完成不代表翼緣已確認。舊結果可能未提供。</summary>
    public bool? ValidationPassed { get; init; }

    /// <summary>截面對齊與翼板支承是否通過。</summary>
    public bool? AlignmentValidationPassed { get; init; }

    /// <summary>翼緣完整性是否通過。</summary>
    public bool? BoundaryValidationPassed { get; init; }

    /// <summary>完成此點雲是否要求翼緣完整性驗證。</summary>
    public bool? BoundaryVerificationRequired { get; init; }

    /// <summary>未通過的品質條件；即使點雲已完成也保留。</summary>
    public IReadOnlyList<ThreeDScan.Domain.Models.StitchQualityFailure>? ValidationFailures { get; init; }

    /// <summary>規格 v1.1 未列，由 Hank 搬入。是否重播。</summary>
    public bool IsReplay { get; init; }

    /// <summary>規格 v1.1 未列，由 Hank 搬入。固定 ConfiguredAbsolute。</summary>
    public string? CoordinateFrame { get; init; }
}
