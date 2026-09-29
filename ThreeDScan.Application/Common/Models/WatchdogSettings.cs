namespace ThreeDScan.Application.Common.Models;

/// <summary>多階段任務閒置逾時（appsettings <c>Watchdog</c>；規格 4.2）。</summary>
public sealed class WatchdogSettings
{
    /// <summary>設定區段名稱。</summary>
    public const string SectionName = "Watchdog";

    /// <summary>自 start 起未 complete／abort 則自動中斷的分鐘數（規格 MaxTaskDuration，預設 10）。</summary>
    public int MaxTaskDurationMinutes { get; set; } = 10;
}
