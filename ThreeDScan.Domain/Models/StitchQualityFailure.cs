namespace ThreeDScan.Domain.Models;

/// <summary>未通過的接縫品質條件；僅保存診斷值，不改變驗證門檻。</summary>
public sealed class StitchQualityFailure
{
    /// <summary>接縫編號，從 1 起算。</summary>
    public int SeamNumber { get; init; }

    /// <summary>穩定的檢查名稱，例如 fullSection.leftEdgeDistanceMm。</summary>
    public string Check { get; init; } = "";

    /// <summary>量測值；無有限值時為 null。</summary>
    public double? ActualValue { get; init; }

    /// <summary>此條件使用的門檻。</summary>
    public double Limit { get; init; }

    /// <summary>通過條件的比較符號：&lt;= 或 &gt;=。</summary>
    public string Comparison { get; init; } = "";

    /// <summary>單位：mm 或 ratio。</summary>
    public string Unit { get; init; } = "";

    /// <summary>含接縫、量測值及門檻的錯誤說明。</summary>
    public string Message { get; init; } = "";
}
