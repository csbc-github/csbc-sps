using System.Text.Json.Serialization;

namespace ThreeDScan.Application.Common.Models;

/// <summary>指南錯誤物件：<c>error.code</c>／<c>error.message</c>。</summary>
public sealed class TaskErrorDto
{
    /// <summary>業務錯誤碼，例如 <c>SURF_001</c>、<c>SYS_NYI</c>。</summary>
    public string Code { get; init; } = "";

    /// <summary>錯誤說明。</summary>
    public string Message { get; init; } = "";

    /// <summary>可選的失敗診斷；無診斷時省略欄位。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? Details { get; init; }
}
