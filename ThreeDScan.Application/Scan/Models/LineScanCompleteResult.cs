namespace ThreeDScan.Application.Scan.Models;

/// <summary>線掃描 complete 最終 result（callback／GET 查詢）。</summary>
public sealed class LineScanCompleteResult
{
    /// <summary>本次偵測到的角鋼總數。</summary>
    public int DetectedCount { get; init; }

    /// <summary>各支角鋼特徵。</summary>
    public IReadOnlyList<LineScanAngleIronDto> AngleIrons { get; init; } = [];
}
