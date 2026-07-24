namespace ThreeDScan.Auth;

/// <summary>WPF 取得 Access Token 的流程名稱（appsettings <c>Authentication:Flow</c>）。</summary>
public static class AuthenticationFlows
{
    /// <summary>人員授權碼：開瀏覽器登入 IdP。</summary>
    public const string Interactive = "Interactive";

    /// <summary>Client Credentials：無人自動以 client_id／secret 換 token。</summary>
    public const string ClientCredentials = "ClientCredentials";
}
