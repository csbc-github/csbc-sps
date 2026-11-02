namespace ThreeDScan.Infrastructure.Export.Models;

/// <summary>上下封口是否量到。</summary>
/// <remarks>由 Hank Scan3D 的 HoleClosure 搬入，邏輯不改。</remarks>
public sealed class HoleClosure
{
    /// <summary>建立封口狀態。</summary>
    /// <param name="lower">下端是否封閉。</param>
    /// <param name="upper">上端是否封閉。</param>
    public HoleClosure(bool lower, bool upper)
    {
        Lower = lower;
        Upper = upper;
    }

    /// <summary>下端是否封閉。</summary>
    public bool Lower { get; init; }

    /// <summary>上端是否封閉。</summary>
    public bool Upper { get; init; }
}
