namespace ThreeDScan.Infrastructure.Export.Models;

/// <summary>平面擬合。對應 Hank <c>Scan3D.Processing.PlaneFit</c>。</summary>
internal sealed class PlaneFit
{
    /// <summary>法線。</summary>
    public ScanVector Normal { get; init; }

    /// <summary>平面常數（n·p = D）。</summary>
    public double D { get; init; }

    /// <summary>RMS（mm）。</summary>
    public double Rms { get; init; }

    /// <summary>參與點索引。</summary>
    public required int[] Indices { get; init; }
}
