namespace ThreeDScan.Infrastructure.Export.Models;

/// <summary>擬合到的鋼面。</summary>
/// <remarks>由 Hank Scan3D 的 HoleFace 搬入，邏輯不改。</remarks>
public sealed class HoleFace
{
    /// <summary>原點。</summary>
    public double[] Origin { get; init; } = [];

    /// <summary>法線。</summary>
    public double[] Normal { get; init; } = [];

    /// <summary>U 軸。</summary>
    public double[] UAxis { get; init; } = [];

    /// <summary>V 軸。</summary>
    public double[] VAxis { get; init; } = [];

    /// <summary>平面常數。</summary>
    public double D { get; init; }

    /// <summary>面識別。</summary>
    public string Id { get; set; } = "";

    /// <summary>擬合點數。</summary>
    public int FitPointCount { get; set; }

    /// <summary>RMS（mm）。</summary>
    public double RmseMm { get; set; }
}
