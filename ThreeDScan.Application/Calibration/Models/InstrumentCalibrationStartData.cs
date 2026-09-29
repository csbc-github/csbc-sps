namespace ThreeDScan.Application.Calibration.Models;

/// <summary>POST /api/calibration/instrument/start 成功回應。</summary>
public sealed class InstrumentCalibrationStartData
{
    /// <summary>等待第二次拍攝。</summary>
    public const string StatusAwaitingNextShot = "AwaitingNextShot";

    /// <summary>本次校正任務識別碼，complete 須帶回。</summary>
    public string TaskId { get; init; } = "";

    /// <summary>目前幀次，固定為 1。</summary>
    public int Seq { get; init; }

    /// <summary>固定 <see cref="StatusAwaitingNextShot"/>。</summary>
    public string Status { get; init; } = "";

    /// <summary>本次呼叫追溯 ID。</summary>
    public string TraceId { get; init; } = "";
}
