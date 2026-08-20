using System.Security.Claims;

namespace ThreeDScan.Auth;

/// <summary>
/// 目前登入工作階段（Access／Refresh／Id Token 與身分）。
/// 以靜態 <see cref="Current"/> 供 WPF 登入與 API／SignalR 共用。
/// </summary>
public sealed class AuthSession
{
    /// <summary>應用程式單一工作階段實例。</summary>
    public static AuthSession Current { get; } = new();

    /// <summary>呼叫業務 API／SignalR 用的 Access Token。</summary>
    public string? AccessToken { get; private set; }

    /// <summary>用於換發新 Access Token（需 IdP 核發；Scope 建議含 offline_access）。</summary>
    public string? RefreshToken { get; private set; }

    /// <summary>OIDC 身分 Token（登入時驗證用）。</summary>
    public string? IdToken { get; private set; }

    /// <summary>Access Token 視為需續期的時間點（已預留緩衝秒數）。</summary>
    public DateTimeOffset? AccessTokenExpiresAt { get; private set; }

    /// <summary>由 id_token 驗證後得到的身分與 claims。</summary>
    public ClaimsPrincipal? Principal { get; private set; }

    /// <summary>已有 Access Token 且 Principal 標記為已驗證。</summary>
    public bool IsAuthenticated
    {
        get
        {
            return !string.IsNullOrEmpty(AccessToken) && Principal?.Identity?.IsAuthenticated == true;
        }
    }

    /// <summary>畫面顯示用名稱（name claim 或 Identity.Name）。</summary>
    public string? DisplayName
    {
        get
        {
            return Principal?.FindFirst(ClaimTypes.Name)?.Value
        ?? Principal?.FindFirst("name")?.Value
        ?? Principal?.Identity?.Name;
        }
    }

    /// <summary>
    /// 寫入登入或 refresh 後的 Token。
    /// <paramref name="expiresInSeconds"/> 有值時，到期時間提前 30 秒以利續期；否則預設約 55 分鐘。
    /// </summary>
    public void SetTokens(
        string accessToken,
        string? refreshToken,
        string? idToken,
        int? expiresInSeconds,
        ClaimsPrincipal? principal)
    {
        AccessToken = accessToken;
        RefreshToken = refreshToken;
        IdToken = idToken;
        Principal = principal;
        AccessTokenExpiresAt = expiresInSeconds is > 0
            ? DateTimeOffset.UtcNow.AddSeconds(expiresInSeconds.Value - 30)
            : DateTimeOffset.UtcNow.AddMinutes(55);
    }

    /// <summary>清除工作階段（登出或重新登入前）。</summary>
    public void Clear()
    {
        AccessToken = null;
        RefreshToken = null;
        IdToken = null;
        AccessTokenExpiresAt = null;
        Principal = null;
    }

    /// <summary>Access Token 已過期或接近過期，應先 Refresh 再使用。</summary>
    public bool AccessTokenNearExpiry
    {
        get
        {
            return AccessTokenExpiresAt is null || DateTimeOffset.UtcNow >= AccessTokenExpiresAt;
        }
    }
}
