namespace ThreeDScan.Application.Common.Models;

/// <summary>API 統一回應包裝（成功／失敗與錯誤碼）。</summary>
public sealed class ApiResponse<T>
{
    /// <summary>是否成功。</summary>
    public bool Success { get; init; }

    /// <summary>回應產生時刻。</summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.Now;

    /// <summary>成功時的資料；失敗時可為診斷資料或 null。</summary>
    public T? Data { get; init; }

    /// <summary>失敗時的業務錯誤碼；成功為 null。</summary>
    public string? ErrorCode { get; init; }

    /// <summary>失敗時的說明；成功為 null。</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>成功結果。</summary>
    public static ApiResponse<T> Ok(T data)
    {
        return new()
        {
            Success = true,
            Data = data,
            Timestamp = DateTimeOffset.Now,
        };
    }

    /// <summary>失敗結果（無附加資料）。</summary>
    public static ApiResponse<T> Fail(string errorCode, string message)
    {
        return new()
        {
            Success = false,
            ErrorCode = errorCode,
            ErrorMessage = message,
            Timestamp = DateTimeOffset.Now,
        };
    }

    /// <summary>失敗結果（附診斷資料）。</summary>
    public static ApiResponse<T> Fail(string errorCode, string message, T data)
    {
        return new()
        {
            Success = false,
            ErrorCode = errorCode,
            ErrorMessage = message,
            Data = data,
            Timestamp = DateTimeOffset.Now,
        };
    }
}
