namespace ThreeDScan.Infrastructure.Export.Models;

/// <summary>規則掃描列孔洞偵測結果。</summary>
/// <remarks>由 Hank Scan3D 的 HoleDetectionResult 搬入，邏輯不改。</remarks>
public sealed class HoleDetectionResult
{
    /// <summary>單位。</summary>
    public string Unit { get; init; } = "mm";

    /// <summary>點數。</summary>
    public int PointCount { get; init; }

    /// <summary>掃描間距。</summary>
    public ScannerPitch ScannerPitch { get; init; } = new ScannerPitch(0, 0);

    /// <summary>偵測參數。</summary>
    public HoleDetectionOptions Options { get; init; } = new HoleDetectionOptions();

    /// <summary>擬合面。</summary>
    public HoleFace[] Faces { get; init; } = [];

    /// <summary>面檢視。</summary>
    public HoleFaceView[] Views { get; init; } = [];

    /// <summary>封閉開口。</summary>
    public DetectedHole[] Holes { get; init; } = [];

    /// <summary>部分開口。</summary>
    public DetectedHole[] PartialHoles { get; init; } = [];

    /// <summary>診斷。</summary>
    public HoleDiagnostics[] Diagnostics { get; init; } = [];

    /// <summary>方法說明。</summary>
    public string Method { get; init; } = "Independent planar faces, original scan-row gaps, measured rim closure";

    /// <summary>限制說明。</summary>
    public string[] Limitations { get; init; } =
    [
        "Openings smaller than the configured size or fewer than four scan rows are not resolved.",
        "No observed opening is not proof that the physical workpiece has no holes; occluded faces are not measured.",
    ];

    /// <summary>全部開口（封閉＋部分），依 Y 再 X 排序。</summary>
    public DetectedHole[] AllOpenings()
    {
        return Holes.Concat(PartialHoles).OrderBy(h => h.Y).ThenBy(h => h.X).ToArray();
    }
}
