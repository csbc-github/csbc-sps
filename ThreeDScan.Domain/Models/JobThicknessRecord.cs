namespace ThreeDScan.Domain.Models;

/// <summary>
/// 工單兩翼厚度。同一 <see cref="JobId"/> 一列。
/// </summary>
public sealed class JobThicknessRecord
{
    /// <summary>工單號。</summary>
    public string JobId { get; init; } = "";

    /// <summary>左側厚度（mm），對應 innerLeft；寫入 JobThickness.ThicknessA。</summary>
    public double ThicknessA { get; init; }

    /// <summary>右側厚度（mm），對應 innerRight；寫入 JobThickness.ThicknessB。</summary>
    public double ThicknessB { get; init; }

    /// <summary>夾具 A 的最近 XY 點是否落在直方圖較低的那翼。</summary>
    public bool ThicknessAOnLowFlange { get; init; }

    /// <summary>寫入此筆時的掃描器編號。</summary>
    public string ScannerId { get; init; } = "";

    /// <summary>量測寫入時間。</summary>
    public DateTimeOffset MeasuredAt { get; init; }

    /// <summary>寫入當次 API 呼叫的追溯 ID。</summary>
    public string TraceId { get; init; } = "";
}
