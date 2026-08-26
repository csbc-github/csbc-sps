namespace ThreeDScan.Domain.Models;

/// <summary>
/// 噴印區厚度量測輸入：夾具點 A／B 的 XYZ。
/// 座標必須與 API 公開點雲（有補償歷程時為已套 offset）同一座標系。
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
