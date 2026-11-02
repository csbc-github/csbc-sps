namespace ThreeDScan.Domain.Models;

/// <summary>信封內的回呼狀態。</summary>
public sealed class TaskCallbackEnvelope
{
    /// <summary>模式（NotRequested／Enabled／PollingOnly）。</summary>
    public string Mode { get; init; } = "NotRequested";

    /// <summary>是否已送達。</summary>
    public bool CallbackDelivered { get; init; }

    /// <summary>已嘗試次數。</summary>
    public int CallbackAttempts { get; init; }

    /// <summary>最後錯誤。</summary>
    public string? CallbackLastError { get; init; }
}
