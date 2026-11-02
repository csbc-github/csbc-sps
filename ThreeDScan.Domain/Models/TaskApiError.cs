namespace ThreeDScan.Domain.Models;

/// <summary>任務失敗內容。對應 Hank <c>Scan3D.Service.ApiError</c>。</summary>
public sealed class TaskApiError
{
    /// <summary>錯誤代碼。</summary>
    public string Code { get; init; } = "";

    /// <summary>錯誤訊息。</summary>
    public string Message { get; init; } = "";

    /// <summary>可選細節（JSON 物件）。</summary>
    public object? Details { get; init; }
}
