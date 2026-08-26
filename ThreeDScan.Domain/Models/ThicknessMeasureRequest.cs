namespace ThreeDScan.Domain.Models;

/// <summary>
/// 厚度量測輸入：夾具點 A／B 的 XYZ（與公開點雲同一座標系）。
/// </summary>
public sealed class ThicknessMeasureRequest
{
    /// <summary>夾具點 A 的 X（mm）。</summary>
    public double Ax { get; init; }

    /// <summary>夾具點 A 的 Y（mm）。</summary>
    public double Ay { get; init; }

    /// <summary>夾具點 A 的 Z（mm）；厚度 = 點雲最近點 Z − 此值。</summary>
    public double Az { get; init; }

    /// <summary>夾具點 B 的 X（mm）。</summary>
    public double Bx { get; init; }

    /// <summary>夾具點 B 的 Y（mm）。</summary>
    public double By { get; init; }

    /// <summary>夾具點 B 的 Z（mm）；厚度 = 點雲最近點 Z − 此值。</summary>
    public double Bz { get; init; }
}
