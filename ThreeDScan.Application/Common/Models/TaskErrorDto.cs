namespace ThreeDScan.Application.Common.Models;

/// <summary>指南錯誤物件：<c>error.code</c>／<c>error.message</c>。</summary>
public sealed class TaskErrorDto
{
    /// <summary>業務錯誤碼，例如 <c>SURF_001</c>、<c>SYS_NYI</c>。</summary>
    public string Code { get; init; } = "";

    /// <summary>錯誤說明。</summary>
    public string Message { get; init; } = "";
}
