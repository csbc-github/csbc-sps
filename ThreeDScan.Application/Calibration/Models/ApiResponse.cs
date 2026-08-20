namespace ThreeDScan.Application.Calibration.Dtos;

/// <summary>校正 API 統一回應包裝（成功／失敗與錯誤碼）。</summary>
public sealed class ApiResponse<T>
{
    public bool Success { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.Now;
    public T? Data { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }

    public static ApiResponse<T> Ok(T data) => new()
    {
        Success = true,
        Data = data,
        Timestamp = DateTimeOffset.Now,
    };

    public static ApiResponse<T> Fail(string errorCode, string message) => new()
    {
        Success = false,
        ErrorCode = errorCode,
        ErrorMessage = message,
        Timestamp = DateTimeOffset.Now,
    };

    public static ApiResponse<T> Fail(string errorCode, string message, T data) => new()
    {
        Success = false,
        ErrorCode = errorCode,
        ErrorMessage = message,
        Data = data,
        Timestamp = DateTimeOffset.Now,
    };
}
