using System.Text.Json;

namespace ThreeDScan.Domain.Models;

/// <summary>面／孔操作的立即回應與任務文件。</summary>
/// <remarks>由 Hank Scan3D 的任務立即回應形狀搬入。</remarks>
public sealed class ScanOutcome
{
    /// <summary>任務文件。</summary>
    public required TaskDocument Document { get; init; }

    /// <summary>start／segment 等同步本體；complete／abort 可為 null。</summary>
    public JsonElement? Response { get; init; }
}
