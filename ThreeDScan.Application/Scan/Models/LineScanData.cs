namespace ThreeDScan.Application.Scan.Models;

/// <summary>線掃描結果：偵測到的角鋼特徵與尺寸。</summary>
public sealed class LineScanData
{
    /// <summary>正常結束。</summary>
    public const string StatusCompleted = "Completed";

    /// <summary>超時結束，部分結果。</summary>
    public const string StatusPartialTimeout = "PartialTimeout";

    /// <summary>本次偵測到的角鋼總數。</summary>
    public int DetectedCount { get; init; }

    /// <summary><see cref="StatusCompleted"/> 或 <see cref="StatusPartialTimeout"/>。</summary>
    public string ScanStatus { get; init; } = "";

    /// <summary>本次呼叫追溯 ID。</summary>
    public string TraceId { get; init; } = "";

    /// <summary>各支角鋼特徵。</summary>
    public IReadOnlyList<AngleIronDto> AngleIrons { get; init; } = [];
}
