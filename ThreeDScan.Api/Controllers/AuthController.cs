using Microsoft.AspNetCore.Mvc;

namespace ThreeDScan.Api.Controllers;

/// <summary>
/// Token 驗證測試 API（規範 H(C)）。
/// 正式授權仍由 JwtBearer 中介軟體處理；本控制器用於驗收與診斷。
/// </summary>
[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    /// <summary>預留診斷端點用。</summary>
    public AuthController()
    {
    }
}
