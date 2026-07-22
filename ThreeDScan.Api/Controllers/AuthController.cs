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

    /// <summary>
    /// 驗證 Bearer Access Token。
    /// 流程：JwtBearer 已通過 → 再以 DAS <c>OpenIdConnectProvider</c> 交叉驗證 → 回傳身分與 claims。
    /// <c>Authentication:Enabled=false</c> 時直接回略過說明（不強制 Token）。
    /// </summary>
    [HttpGet("validate")]
    [Produces("application/json")]
    public async Task<IActionResult> Validate()
    {
        if (!_options.Enabled)
        {
            return Ok(new
            {
                valid = true,
                authenticationEnabled = false,
                message = "Authentication:Enabled=false，略過 Token 驗證（本機開發）。",
            });
        }

        if (User.Identity?.IsAuthenticated != true)
            return Unauthorized(new { valid = false, message = "缺少或無效的 Bearer Token。" });

        var bearer = GetBearerToken();
        ClaimsPrincipal? libPrincipal = null;
        string? libError = null;

        // 交叉驗證：JwtBearer 已過關後，再以 DAS 函式庫驗一次（診斷用，不以它取代 JwtBearer）
        if (!string.IsNullOrEmpty(bearer) && !string.IsNullOrWhiteSpace(_options.Authority))
        {
            try
            {
                libPrincipal = await _authManager.ValidateTokenAsync("oidc", bearer);
            }
            catch (Exception ex)
            {
                libError = ex.Message;
            }
        }

        return Ok(new
        {
            valid = true,
            authenticationEnabled = true,
            scheme = User.Identity?.AuthenticationType,
            name = User.Identity?.Name
                   ?? User.FindFirst("name")?.Value
                   ?? User.FindFirst(ClaimTypes.Name)?.Value,
            subject = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                      ?? User.FindFirst("sub")?.Value,
            claims = User.Claims.Select(c => new { c.Type, c.Value }).ToList(),
            dasLibrary = new
            {
                validated = libPrincipal?.Identity?.IsAuthenticated == true,
                name = libPrincipal?.Identity?.Name,
                error = libError,
            },
        });
    }

    /// <summary>簡易探測端點（啟用驗證時與其他 API 同樣需 Bearer）。</summary>
    [HttpGet("ping")]
    [Produces("application/json")]
    public IActionResult Ping() => Ok(new { ok = true, utc = DateTime.UtcNow });

    /// <summary>自 <c>Authorization</c> header 取出 Bearer token 字串。</summary>
    private string? GetBearerToken()
    {
        var header = Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(header))
            return null;
        const string prefix = "Bearer ";
        return header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? header[prefix.Length..].Trim()
            : null;
    }
}
