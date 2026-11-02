namespace ThreeDScan.Infrastructure.Export.Models;

/// <summary>兩翼擬合。對應 Hank <c>Scan3D.Processing.WingFit</c>。</summary>
internal sealed class WingFit
{
    /// <summary>左翼平面。</summary>
    public required PlaneFit Left { get; init; }

    /// <summary>右翼平面。</summary>
    public required PlaneFit Right { get; init; }

    /// <summary>稜線原點。</summary>
    public ScanVector RidgeOrigin { get; init; }

    /// <summary>長軸。</summary>
    public ScanVector LongAxis { get; init; }

    /// <summary>橫軸。</summary>
    public ScanVector CrossAxis { get; init; }

    /// <summary>上軸。</summary>
    public ScanVector UpAxis { get; init; }

    /// <summary>兩翼法線夾角（度）。</summary>
    public double NormalAngle { get; init; }

    /// <summary>參與點。</summary>
    public required ScanVector[] Points { get; init; }

    /// <summary>原始 PLY 索引。</summary>
    public required int[] RawIndices { get; init; }
}
