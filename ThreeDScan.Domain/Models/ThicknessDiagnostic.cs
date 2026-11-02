namespace ThreeDScan.Domain.Models;

/// <summary>單側厚度診斷。對應 Hank <c>Scan3D.Processing.ThicknessDiagnostic</c>。</summary>
public sealed class ThicknessDiagnostic
{
    /// <summary>是否算出厚度。</summary>
    public bool Available { get; init; }

    /// <summary>厚度（mm）。</summary>
    public double? ThicknessMm { get; init; }

    /// <summary>內側點（掃描器座標）。</summary>
    public Point3? InnerPointScanner { get; init; }

    /// <summary>投影到外側平面的點。</summary>
    public Point3? ProjectedOuterPointScanner { get; init; }

    /// <summary>最近外側量測點。</summary>
    public Point3? NearestMeasuredOuterPointScanner { get; init; }

    /// <summary>最近量測距離（mm）。</summary>
    public double? NearestMeasurementDistanceMm { get; init; }

    /// <summary>平面 RMS（mm）。</summary>
    public double? PlaneRmsMm { get; init; }

    /// <summary>取樣點數。</summary>
    public int SampleCount { get; init; }

    /// <summary>取樣來源點索引。</summary>
    public int[] SampleSourcePointIndices { get; init; } = Array.Empty<int>();

    /// <summary>失敗原因。</summary>
    public string? Reason { get; init; }

    /// <summary>是否使用平面延伸（非實測）。</summary>
    public bool UsesPlaneExtension { get; init; }
}
