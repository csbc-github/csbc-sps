using Microsoft.AspNetCore.Mvc;
using ThreeDScan.Application.Calibration.Models;
using ThreeDScan.Application.Common;

namespace ThreeDScan.Api.Controllers;

/// <summary>將應用服務結果對應為 HTTP 200／400；尚未實作則 501。</summary>
internal static class SpecApiActionResult
{
    public static IActionResult From<T>(ApiResponse<T> result)
    {
        if (result.ErrorCode == SpecNotImplemented.ErrorCode)
            return new ObjectResult(result) { StatusCode = StatusCodes.Status501NotImplemented };

        return result.Success
            ? new OkObjectResult(result)
            : new BadRequestObjectResult(result);
    }
}
