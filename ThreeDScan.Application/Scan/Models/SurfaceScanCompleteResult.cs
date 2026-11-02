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

    /// <summary>規格 v1.1 未列，由 Hank 搬入。點數。</summary>
    public int PointCount { get; init; }

    /// <summary>規格 v1.1 未列，由 Hank 搬入。是否已現場校正。</summary>
    public bool AbsoluteCoordinatesCalibrated { get; init; }

    /// <summary>規格 v1.1 未列，由 Hank 搬入。板厚（mm）。</summary>
    public double? ThicknessMm { get; init; }

    /// <summary>規格 v1.1 未列，由 Hank 搬入。警告。</summary>
    public IReadOnlyList<string>? Warnings { get; init; }

    /// <summary>規格 v1.1 未列，由 Hank 搬入。是否重播。</summary>
    public bool IsReplay { get; init; }

    /// <summary>規格 v1.1 未列，由 Hank 搬入。固定 ConfiguredAbsolute。</summary>
    public string? CoordinateFrame { get; init; }
}
