namespace ThreeDScan.Domain.Models;

/// <summary>SDK 尋球量測結果（球心清單、合格球數與基準面）。</summary>
public sealed class SphereMeasureResult
{
    /// <summary>掃描器編號。</summary>
    public required string ScannerId { get; init; }

    /// <summary>量到的球。</summary>
    public IReadOnlyList<MeasuredBall> Balls { get; init; } = Array.Empty<MeasuredBall>();

    /// <summary>通過容差的球數。</summary>
    public int KeptCount { get; init; }

    /// <summary>基準面（滾輪）平面量測；未啟用或失敗時為 null。</summary>
    public DatumPlaneMeasureResult? DatumPlane { get; init; }
}
