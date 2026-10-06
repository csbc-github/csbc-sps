namespace ThreeDScan.Domain.Models;

/// <summary>一次空滾輪取樣的擬合結果。</summary>
public sealed class RollerSampleResult
{
    /// <summary>輪廓夠平、判定滾輪已清空。</summary>
    public bool Clear { get; init; }

    /// <summary>失敗時的錯誤碼；成功為空。</summary>
    public string Code { get; init; } = "";

    /// <summary>失敗說明；成功為空。</summary>
    public string Message { get; init; } = "";

    /// <summary>掃描器到滾輪平面的距離 (mm)。</summary>
    public double ZDistance { get; init; }

    /// <summary>輪廓變異 (mm)，取殘差的第 95 百分位。</summary>
    public double FlatnessMm { get; init; }

    /// <summary>滾輪直線 ax+by+c=0 的 a。</summary>
    public double LineA { get; init; }

    /// <summary>滾輪直線的 b。</summary>
    public double LineB { get; init; }

    /// <summary>滾輪直線的 c。</summary>
    public double LineC { get; init; }
}
