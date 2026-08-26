namespace ThreeDScan.Infrastructure.Repositories.Models;

/// <summary>對應資料表 <c>JobThickness</c>：一筆工單列存兩翼厚度。</summary>
public sealed class JobThicknessEntity
{
    /// <summary>主鍵：工單號。</summary>
    public string JobId { get; set; } = "";

    /// <summary>翼 A 厚度（mm）。</summary>
    public double ThicknessA { get; set; }

    /// <summary>翼 B 厚度（mm）。</summary>
    public double ThicknessB { get; set; }

    /// <summary>夾具 A 是否對到直方圖較低的那翼。</summary>
    public bool ThicknessAOnLowFlange { get; set; }

    /// <summary>寫入時的掃描器編號。</summary>
    public string ScannerId { get; set; } = "";

    /// <summary>寫入時間。</summary>
    public DateTimeOffset MeasuredAt { get; set; }

    /// <summary>寫入當次追溯 ID。</summary>
    public string TraceId { get; set; } = "";
}
