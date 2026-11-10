using Microsoft.AspNetCore.Mvc;
using ThreeDScan.Application.Common;
using ThreeDScan.Application.Common.Models;

namespace ThreeDScan.Api.Common;

/// <summary>將 <see cref="ApiResponse{T}"/> 對應為指南 HTTP：成功回資料本身，失敗回 <c>error.code</c>。</summary>
internal static class SpecApiActionResult
{
    /// <summary>成功 200 回 <c>Data</c>；尚未實作 501；其他失敗 400。本體不含 success、errorCode。</summary>
    /// <param name="result">應用服務結果。</param>
    /// <returns>對應狀態碼的 HTTP 結果。</returns>
    public static IActionResult From<T>(ApiResponse<T> result)
    {
        if (result.ErrorCode == SpecNotImplemented.ErrorCode
            || string.Equals(result.ErrorCode, "EDGE_NOTCH_UNVERIFIED", StringComparison.Ordinal))
        {
            return new ObjectResult(ToErrorBody(result))
            {
                StatusCode = StatusCodes.Status501NotImplemented,
            };
        }

        if (!result.Success)
        {
            return new BadRequestObjectResult(ToErrorBody(result));
        }

        return new OkObjectResult(result.Data);
    }

    /// <summary>把錯誤碼與說明收成指南 <c>{ error }</c> 本體。</summary>
    /// <param name="result">失敗的應用服務結果。</param>
    /// <returns>僅含 error 的回應本體。</returns>
    private static DasErrorBody ToErrorBody<T>(ApiResponse<T> result)
    {
        var code = result.ErrorCode;
        var message = result.ErrorMessage;
        return new DasErrorBody
        {
            Error = new TaskErrorDto
            {
                Code = string.IsNullOrWhiteSpace(code) ? SpecNotImplemented.ErrorCode : code,
                Message = string.IsNullOrWhiteSpace(message) ? "未知錯誤。" : message,
            },
        };
    }
}
