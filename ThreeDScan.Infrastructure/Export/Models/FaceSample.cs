namespace ThreeDScan.Infrastructure.Export.Models;

/// <summary>面上取樣點（UV）。</summary>
/// <remarks>由 Hank Scan3D 的 FaceSample 搬入，邏輯不改。</remarks>
public sealed class FaceSample
{
    /// <summary>建立取樣點。</summary>
    /// <param name="u">U。</param>
    /// <param name="v">V。</param>
    public FaceSample(double u, double v)
    {
        U = u;
        V = v;
    }

    /// <summary>U。</summary>
    public double U { get; init; }

    /// <summary>V。</summary>
    public double V { get; init; }
}
