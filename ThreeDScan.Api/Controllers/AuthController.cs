using System.Security.Claims;
using DAS.Authentication.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using ThreeDScan.Api.Auth;

namespace ThreeDScan.Api.Controllers;

/// <summary>
/// Token 驗證測試 API（規範 H(C)）。
/// 正式授權仍由 JwtBearer 中介軟體處理；本控制器用於驗收與診斷。
/// </summary>
[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly AuthenticationOptions _options;
    private readonly AuthenticationManager _authManager;

    /// <summary>注入 Authentication 選項與 DAS 驗證管理員。</summary>
    public AuthController(IOptions<AuthenticationOptions> options, AuthenticationManager authManager)
    {
        _options = options.Value;
        _authManager = authManager;
    }

    // 診斷用 GET /api/auth/validate 已停用；正式仍由 JwtBearer 對各業務 API 驗證 Bearer。
    // [HttpGet("validate")]
    // [Produces("application/json")]
    // public async Task<IActionResult> Validate()
    // {
    //     if (!_options.Enabled)
    //     {
    //         return Ok(new
    //         {
    //             valid = true,
    //             authenticationEnabled = false,
    //             message = "Authentication:Enabled=false，略過 Token 驗證（本機開發）。",
    //         });
    //     }
    //
    //     if (User.Identity?.IsAuthenticated != true)
    //         return Unauthorized(new { valid = false, message = "缺少或無效的 Bearer Token。" });
    //
    //     var bearer = GetBearerToken();
    //     ClaimsPrincipal? libPrincipal = null;
    //     string? libError = null;
    //
    //     if (!string.IsNullOrEmpty(bearer) && !string.IsNullOrWhiteSpace(_options.Authority))
    //     {
    //         try
    //         {
    //             libPrincipal = await _authManager.ValidateTokenAsync("oidc", bearer);
    //         }
    //         catch (Exception ex)
    //         {
    //             libError = ex.Message;
    //         }
    //     }
    //
    //     return Ok(new
    //     {
    //         valid = true,
    //         authenticationEnabled = true,
    //         scheme = User.Identity?.AuthenticationType,
    //         name = User.Identity?.Name
    //                ?? User.FindFirst("name")?.Value
    //                ?? User.FindFirst(ClaimTypes.Name)?.Value,
    //         subject = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
    //                   ?? User.FindFirst("sub")?.Value,
    //         claims = User.Claims.Select(c => new { c.Type, c.Value }).ToList(),
    //         dasLibrary = new
    //         {
    //             validated = libPrincipal?.Identity?.IsAuthenticated == true,
    //             name = libPrincipal?.Identity?.Name,
    //             error = libError,
    //         },
    //     });
    // }
    //
    // private string? GetBearerToken()
    // {
    //     var header = Request.Headers.Authorization.ToString();
    //     if (string.IsNullOrWhiteSpace(header))
    //         return null;
    //     const string prefix = "Bearer ";
    //     return header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
    //         ? header[prefix.Length..].Trim()
    //         : null;
    // }
}
