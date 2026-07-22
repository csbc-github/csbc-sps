namespace ThreeDScan.Api.Auth;

/// <summary>
/// 甲方 OAuth2.0／OIDC IdP 設定（對應 appsettings <c>Authentication</c> 區段）。
/// 與 WPF <c>ThreeDScan.Auth.AuthenticationOptions</c> 對齊；Api 端以 JwtBearer 驗證為主，
/// 故不需 RedirectUri／ClientSecret／Scope（那些供桌面授權碼流程使用）。
/// </summary>
public sealed class AuthenticationOptions
{
    /// <summary>設定區段名稱：<c>Authentication</c>。</summary>
    public const string SectionName = "Authentication";

    /// <summary>
    /// <c>false</c>：不註冊 JwtBearer、不強制 Controllers／Hub 授權。
    /// <c>true</c>：需 Bearer；並要求設定 <see cref="Authority"/>。
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>IdP Authority（JwtBearer 的 issuer／metadata 來源）。</summary>
    public string Authority { get; set; } = "";

    /// <summary>Client Id；供 DAS <c>OpenIdConnectProvider</c> 交叉驗證時作為 audience 參考。</summary>
    public string ClientId { get; set; } = "";

    /// <summary>Access Token 預期 audience；空白則不驗證 Audience。</summary>
    public string? Audience { get; set; }

    /// <summary>JwtBearer 是否要求 HTTPS metadata。</summary>
    public bool RequireHttpsMetadata { get; set; } = true;
}
