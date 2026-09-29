namespace ThreeDScan.Application.Common.Models;

/// <summary>指南同步錯誤／501 回應本體。</summary>
public sealed class DasErrorBody
{
    /// <summary>錯誤明細。</summary>
    public TaskErrorDto Error { get; init; } = new();
}
