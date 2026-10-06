namespace ThreeDScan.Application.Scan.Models;

/// <summary>進料區左側即時剖面。錄製中帶最新一圈截面，否則由畫面自己連光達。</summary>
public sealed class LineScanLiveFrame
{
    /// <summary>是否有進行中的錄製佔用光達。</summary>
    public bool Recording { get; init; }

    /// <summary>進行中的任務識別碼；沒有錄製時為空。</summary>
    public string TaskId { get; init; } = "";

    /// <summary>最新一圈有效點的 X（mm），與 <see cref="Y"/> 對齊。</summary>
    public double[] X { get; init; } = [];

    /// <summary>最新一圈有效點的 Y（mm），與 <see cref="X"/> 對齊。</summary>
    public double[] Y { get; init; } = [];
}
