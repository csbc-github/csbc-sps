namespace ThreeDScan.Domain.Models;

/// <summary>
/// 厚度量測輸入：測距儀內表面點（規格 innerLeft／innerRight）。
/// </summary>
public sealed class ThicknessMeasureRequest
{
    /// <summary>是否帶左側內表面點。</summary>
    public bool HasInnerLeft { get; init; }

    /// <summary>左側測距點 X（mm）。</summary>
    public double InnerLeftX { get; init; }

    /// <summary>左側測距點 Y（mm）。</summary>
    public double InnerLeftY { get; init; }

    /// <summary>左側測距點 Z（mm）。</summary>
    public double InnerLeftZ { get; init; }

    /// <summary>是否帶右側內表面點。</summary>
    public bool HasInnerRight { get; init; }

    /// <summary>右側測距點 X（mm）。</summary>
    public double InnerRightX { get; init; }

    /// <summary>右側測距點 Y（mm）。</summary>
    public double InnerRightY { get; init; }

    /// <summary>右側測距點 Z（mm）。</summary>
    public double InnerRightZ { get; init; }
}
