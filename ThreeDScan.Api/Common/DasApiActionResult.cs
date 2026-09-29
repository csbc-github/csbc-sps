using Microsoft.AspNetCore.Mvc;
using ThreeDScan.Application.Common;
using ThreeDScan.Application.Common.Models;

namespace ThreeDScan.Api.Common;

/// <summary>將指南路徑結果對應為 HTTP 200／202／400／501，本體不包 ApiResponse。</summary>
internal static class DasApiActionResult
{
    /// <summary>同步成功 200；尚未實作 501；其他失敗 400。</summary>
    public static IActionResult From<T>(DasOperationResult<T> result)
    {
        if (result.IsNotImplemented)
        {
            return new ObjectResult(ToErrorBody(result))
            {
                StatusCode = StatusCodes.Status501NotImplemented,
            };
        }

        if (result.Error != null)
        {
            return new BadRequestObjectResult(ToErrorBody(result));
        }

        return new OkObjectResult(result.Data);
    }

    /// <summary>接受處理 202；尚未實作 501；其他失敗 400。</summary>
    public static IActionResult Accepted<T>(DasOperationResult<T> result)
    {
        if (result.IsNotImplemented || result.Error != null)
        {
            return From(result);
        }

        return new ObjectResult(result.Data)
        {
            StatusCode = StatusCodes.Status202Accepted,
        };
    }

    /// <summary>將應用層錯誤轉成指南 <c>{ error }</c> 本體。</summary>
    private static DasErrorBody ToErrorBody<T>(DasOperationResult<T> result)
    {
        return new DasErrorBody
        {
            Error = result.Error ?? new TaskErrorDto
            {
                Code = SpecNotImplemented.ErrorCode,
                Message = "未知錯誤。",
            },
        };
    }
}
