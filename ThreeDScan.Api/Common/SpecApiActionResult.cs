using Microsoft.AspNetCore.Mvc;
using ThreeDScan.Application.Common;
using ThreeDScan.Application.Common.Models;

namespace ThreeDScan.Api.Common;

/// <summary>將應用服務結果對應為 HTTP 200／400；尚未實作則 501。</summary>
internal static class SpecApiActionResult
{
    /// <summary>成功 200；尚未實作 501；其他失敗 400。</summary>
    public static IActionResult From<T>(ApiResponse<T> result)
    {
        if (result.ErrorCode == SpecNotImplemented.ErrorCode)
        {
            return new ObjectResult(result) { StatusCode = StatusCodes.Status501NotImplemented };
        }

        if (result.Success)
        {
            return new OkObjectResult(result);
        }

        return new BadRequestObjectResult(result);
    }
}
