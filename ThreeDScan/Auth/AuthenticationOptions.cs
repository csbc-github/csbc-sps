namespace ThreeDScan.Auth;

/// <summary>甲方 OAuth2.0／OIDC IdP 設定（對應 appsettings <c>Authentication</c> 區段）。</summary>
public sealed class AuthenticationOptions
{
    /// <summary>設定區段名稱：<c>Authentication</c>。</summary>
    public const string SectionName = "Authentication";

    /// <summary>
    /// <c>false</c>：略過 WPF 登入與 API JWT（本機開發）。
    /// <c>true</c>：啟動需取得 IdP Token；呼叫 API／Hub 需 Bearer。
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// 取得 Token 的方式：
    /// <see cref="AuthenticationFlows.Interactive"/>（瀏覽器人員登入）或
    /// <see cref="AuthenticationFlows.ClientCredentials"/>（無人全自動）。
    /// </summary>
    public string Flow { get; set; } = AuthenticationFlows.Interactive;

    /// <summary>IdP Authority（issuer 基底），例如 <c>https://idp.example/</c>。</summary>
    public string Authority { get; set; } = "";

    /// <summary>在 IdP 登錄的 Client Id。</summary>
    public string ClientId { get; set; } = "";

    /// <summary>機密 Client 的 secret（授權碼換 token、Client Credentials、refresh 時使用）。</summary>
    public string ClientSecret { get; set; } = "";

    /// <summary>WPF loopback redirect URI（僅 Interactive）；須事先在 IdP Client 登錄。</summary>
    public string RedirectUri { get; set; } = "http://127.0.0.1:5005/";

    /// <summary>Interactive 授權 scope；含 <c>offline_access</c> 才通常核發 Refresh Token。</summary>
    public string Scope { get; set; } = "openid profile email offline_access";

    /// <summary>
    /// Client Credentials 專用 scope（選填）。空白則沿用 <see cref="Scope"/>。
    /// 機器對機器常為 Api scope（例如 <c>api.read</c>），不一定需要 openid。
    /// </summary>
    public string? ClientCredentialsScope { get; set; }

    /// <summary>Access Token audience；空白則 Api 端僅驗證 issuer／簽章／lifetime。</summary>
    public string? Audience { get; set; }

    /// <summary>是否要求 IdP metadata 使用 HTTPS（正式環境建議 <c>true</c>）。</summary>
    public bool RequireHttpsMetadata { get; set; } = true;

    /// <summary>是否為 Client Credentials 全自動流程。</summary>
    public bool IsClientCredentialsFlow
    {
        get
        {
            return string.Equals(Flow, AuthenticationFlows.ClientCredentials, StringComparison.OrdinalIgnoreCase);
        }
    }
}
