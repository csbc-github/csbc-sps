using ThreeDScan.Application.Common;

namespace ThreeDScan.Application.Common.Models;

/// <summary>指南路徑應用層結果：成功資料或 <see cref="TaskErrorDto"/>。</summary>
public sealed class DasOperationResult<T>
{
    /// <summary>成功時的回應本體。</summary>
    public T? Data { get; init; }

    /// <summary>失敗或尚未實作時的錯誤。</summary>
    public TaskErrorDto? Error { get; init; }

    /// <summary>是否為尚未實作。</summary>
    public bool IsNotImplemented
    {
        get
        {
            return Error != null && Error.Code == SpecNotImplemented.ErrorCode;
        }
    }

    /// <summary>是否成功（有資料且無錯誤）。</summary>
    public bool Success
    {
        get
        {
            return Error == null && Data != null;
        }
    }

    /// <summary>成功結果。</summary>
    public static DasOperationResult<T> Ok(T data)
    {
        return new DasOperationResult<T>
        {
            Data = data,
        };
    }

    /// <summary>失敗結果。</summary>
    public static DasOperationResult<T> Fail(string code, string message)
    {
        return new DasOperationResult<T>
        {
            Error = new TaskErrorDto
            {
                Code = code,
                Message = message,
            },
        };
    }

    /// <summary>尚未實作。</summary>
    public static DasOperationResult<T> NotImplemented(string endpoint)
    {
        return Fail(SpecNotImplemented.ErrorCode, $"{endpoint} 尚未實作。");
    }
}
