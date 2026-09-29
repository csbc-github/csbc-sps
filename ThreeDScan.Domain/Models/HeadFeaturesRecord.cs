namespace ThreeDScan.Domain.Models;

/// <summary>
/// 本段前緣三點（公開絕對座標 mm）與沿長度包角線最大 Z 差。
/// </summary>
public sealed class HeadFeaturesRecord
{
    /// <summary><c>angle</c> 或 <c>envelope</c>。</summary>
    public string Kind { get; init; } = "envelope";

    /// <summary>左邊角點 X (mm)。</summary>
    public double LeftX { get; init; }

    /// <summary>左邊角點 Y (mm)。</summary>
    public double LeftY { get; init; }

    /// <summary>左邊角點 Z (mm)。</summary>
    public double LeftZ { get; init; }

    /// <summary>頂點 X (mm)。</summary>
    public double ApexX { get; init; }

    /// <summary>頂點 Y (mm)。</summary>
    public double ApexY { get; init; }

    /// <summary>頂點 Z (mm)。</summary>
    public double ApexZ { get; init; }

    /// <summary>右邊角點 X (mm)。</summary>
    public double RightX { get; init; }

    /// <summary>右邊角點 Y (mm)。</summary>
    public double RightY { get; init; }

    /// <summary>右邊角點 Z (mm)。</summary>
    public double RightZ { get; init; }

    /// <summary>本段沿移動方向各薄片最大 Z 的 max−min（mm）。</summary>
    public double RidgeZDelta { get; init; }

    /// <summary>本段沿長度軸包絡跨距（mm）；不是 ridgeZDelta。</summary>
    public double Length { get; init; }
}
