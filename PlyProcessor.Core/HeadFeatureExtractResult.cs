namespace PlyProcessor.Core;

/// <summary>
/// 本段前緣三點（掃描器座標 mm）與沿長度包角線的最大 Z 差。
/// </summary>
public sealed class HeadFeatureExtractResult
{
    /// <summary><c>angle</c> 倒 V；<c>envelope</c> 不像角鋼時的包絡點。</summary>
    public string Kind { get; init; } = "envelope";

    public float LeftX { get; init; }

    public float LeftY { get; init; }

    public float LeftZ { get; init; }

    public float ApexX { get; init; }

    public float ApexY { get; init; }

    public float ApexZ { get; init; }

    public float RightX { get; init; }

    public float RightY { get; init; }

    public float RightZ { get; init; }

    /// <summary>沿移動方向各薄片最大 Z 的 max−min（mm）。</summary>
    public float RidgeZDelta { get; init; }

    /// <summary>本段沿 XY 長度軸的包絡跨距（mm）。</summary>
    public float SpanMm { get; init; }
}
