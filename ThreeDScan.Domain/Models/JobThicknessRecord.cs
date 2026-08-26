namespace ThreeDScan.Domain.Models;

/// <summary>
/// 噴印區量得、寫入資料庫的兩翼厚度。
/// 同一 <see cref="JobId"/> 在割切區 <c>measureThickness=false</c> 時用來組 STEP／GLB，不再現場量測。
/// </summary>
public sealed class JobThicknessRecord
{
    /// <summary>台船工單號；噴印／割切／研磨同一根角鋼必須相同。</summary>
    public string JobId { get; init; } = "";

    /// <summary>翼 A 厚度（mm），對應噴印夾具點 A。</summary>
    public double ThicknessA { get; init; }

    /// <summary>翼 B 厚度（mm），對應噴印夾具點 B。</summary>
    public double ThicknessB { get; init; }

    /// <summary>
    /// 噴印量測時，夾具 A 的最近 XY 點是否落在直方圖「較低」的那翼。
    /// 割切組網格時沿用此對應，避免割切點雲沒有夾具 XY 卻對錯翼。
    /// </summary>
    public bool ThicknessAOnLowFlange { get; init; }

    /// <summary>寫入此筆時的掃描器編號（通常為噴印區）。</summary>
    public string ScannerId { get; init; } = "";

    /// <summary>量測寫入時間。</summary>
    public DateTimeOffset MeasuredAt { get; init; }

    /// <summary>寫入當次 API 呼叫的追溯 ID。</summary>
    public string TraceId { get; init; } = "";
}
