using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text;
using DAS.Authentication.Services;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace ThreeDScan.Auth;

/// <summary>
/// WPF Authorization Code Flow：開啟瀏覽器登入甲方 IDP，
/// 以 loopback 接 code 後透過 <see cref="OidcCodeFlowService"/> 換取 Token，
/// 並寫入 <see cref="AuthSession"/>；另支援 Refresh Token 續期。
/// </summary>
public sealed class OidcDesktopLoginService
{
    private readonly AuthenticationOptions _options;
    private readonly OidcCodeFlowService _oidc = new();

    /// <summary>以 appsettings 的 Authentication 區段建立登入服務。</summary>
    public OidcDesktopLoginService(AuthenticationOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>
    /// 執行 OIDC 授權碼登入：開瀏覽器 → 本機接 code → 換 token → 驗 id_token → 寫入工作階段。
    /// </summary>
    /// <returns>已取得 Access Token（或身分已驗證）時為 <c>true</c>；使用者取消／IdP 錯誤／換 token 失敗為 <c>false</c>。</returns>
    public async Task<bool> LoginAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.Authority) || string.IsNullOrWhiteSpace(_options.ClientId))
            throw new InvalidOperationException("請先在 appsettings 設定 Authentication:Authority 與 ClientId。");

        // CSRF 防護：授權請求與 redirect 回傳的 state 必須一致
        var state = OidcCodeFlowService.GenerateState();
        // 重放防護：寫入授權請求，之後與 id_token 內的 nonce 比對
        var nonce = OidcCodeFlowService.GenerateNonce();
        var redirectUri = NormalizeRedirectUri(_options.RedirectUri);

        // 從 IdP discovery 組出授權端點 URL（含 client_id、redirect_uri、scope、state、nonce）
        var authUrl = await _oidc.BuildAuthorizationUrlAsync(
            _options.Authority,
            _options.ClientId,
            redirectUri,
            state,
            nonce,
            _options.Scope);

        // 本機 loopback：IdP 登入成功後會 redirect 到此，帶回 authorization code
        using var listener = new HttpListener();
        listener.Prefixes.Add(EnsureTrailingSlash(redirectUri));
        listener.Start();

        // 開啟系統預設瀏覽器，導向甲方 IdP 登入頁
        Process.Start(new ProcessStartInfo
        {
            FileName = authUrl,
            UseShellExecute = true,
        });

        // 等待瀏覽器 redirect 回來（含 code 或 error）
        var context = await listener.GetContextAsync().WaitAsync(cancellationToken);
        var error = GetQueryValue(context.Request.Url, "error");
        var code = GetQueryValue(context.Request.Url, "code");
        var returnedState = GetQueryValue(context.Request.Url, "state");

        await WriteBrowserResponseAsync(context.Response, string.IsNullOrEmpty(error));
        listener.Stop();

        if (!string.IsNullOrEmpty(error) || string.IsNullOrEmpty(code) || returnedState != state)
            return false;

        // 以 authorization code + client_secret 向 token_endpoint 換取 Access／Refresh／Id Token
        var tokens = await _oidc.ExchangeCodeAsync(
            _options.Authority,
            _options.ClientId,
            _options.ClientSecret,
            code,
            redirectUri);

        if (tokens is null || string.IsNullOrEmpty(tokens.AccessToken))
            return false;

        // 驗證 id_token（簽章、issuer、audience、nonce）並取得 ClaimsPrincipal
        var principal = await _oidc.ValidateIdTokenAsync(
            _options.Authority,
            _options.ClientId,
            tokens.IdToken,
            nonce,
            _options.ClientSecret);

        AuthSession.Current.SetTokens(
            tokens.AccessToken,
            tokens.RefreshToken,
            tokens.IdToken,
            tokens.ExpiresIn,
            principal);

        return AuthSession.Current.IsAuthenticated
               || !string.IsNullOrEmpty(AuthSession.Current.AccessToken);
    }

    /// <summary>
    /// 以目前工作階段的 Refresh Token 向 IdP 換發新的 Access Token（規範 H(B)）。
    /// Scope 需含 <c>offline_access</c> 才通常會取得 refresh_token。
    /// </summary>
    /// <returns>換發成功並已更新 <see cref="AuthSession"/> 時為 <c>true</c>。</returns>
    public async Task<bool> RefreshAsync(CancellationToken cancellationToken = default)
    {
        var refresh = AuthSession.Current.RefreshToken;
        if (string.IsNullOrEmpty(refresh))
            return false;

        var config = await GetConfigurationAsync(_options.Authority, cancellationToken);
        var tokenEndpoint = config.TokenEndpoint
            ?? throw new InvalidOperationException("無法從 IDP discovery 取得 token_endpoint。");

        using var http = CreateHttpClient();
        using var response = await http.PostAsync(
            tokenEndpoint,
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refresh,
                ["client_id"] = _options.ClientId,
                ["client_secret"] = _options.ClientSecret,
            }),
            cancellationToken);

        if (!response.IsSuccessStatusCode)
            return false;

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var doc = await System.Text.Json.JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = doc.RootElement;

        var access = root.TryGetProperty("access_token", out var at) ? at.GetString() : null;
        if (string.IsNullOrEmpty(access))
            return false;

        // 部分 IdP 會輪替 refresh_token；未回傳則沿用舊值
        var newRefresh = root.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : refresh;
        var idToken = root.TryGetProperty("id_token", out var id) ? id.GetString() : AuthSession.Current.IdToken;
        var expiresIn = root.TryGetProperty("expires_in", out var exp) ? exp.GetInt32() : (int?)null;

        AuthSession.Current.SetTokens(
            access,
            newRefresh,
            idToken,
            expiresIn,
            AuthSession.Current.Principal);

        return true;
    }

    /// <summary>
    /// 取得可用的 Access Token；若接近過期則先嘗試 Refresh。
    /// 供 HttpClient／SignalR <c>AccessTokenProvider</c> 呼叫。
    /// </summary>
    /// <returns>有效 Access Token；無 token 或 refresh 失敗時為 <c>null</c>。</returns>
    public async Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(AuthSession.Current.AccessToken))
            return null;

        if (AuthSession.Current.AccessTokenNearExpiry)
        {
            if (!await RefreshAsync(cancellationToken))
                return null;
        }

        return AuthSession.Current.AccessToken;
    }

    /// <summary>回應瀏覽器：提示使用者可關閉頁面並返回應用程式。</summary>
    private static async Task WriteBrowserResponseAsync(HttpListenerResponse response, bool success)
    {
        response.ContentType = "text/html; charset=utf-8";
        response.ContentEncoding = Encoding.UTF8;
        var html = success
            ? """
              <!DOCTYPE html><html lang="zh-Hant"><head><meta charset="utf-8"/><title>登入成功</title></head>
              <body style="font-family:Microsoft JhengHei,sans-serif;text-align:center;padding:48px;">
              <h1>登入成功</h1><p>您可以關閉此視窗並返回應用程式。</p></body></html>
              """
            : """
              <!DOCTYPE html><html lang="zh-Hant"><head><meta charset="utf-8"/><title>登入失敗</title></head>
              <body style="font-family:Microsoft JhengHei,sans-serif;text-align:center;padding:48px;">
              <h1>登入失敗</h1><p>請關閉此視窗並於應用程式重試。</p></body></html>
              """;
        var buffer = Encoding.UTF8.GetBytes(html);
        response.ContentLength64 = buffer.Length;
        await response.OutputStream.WriteAsync(buffer);
        response.OutputStream.Close();
    }

    /// <summary>讀取 IdP OpenID Connect discovery（<c>/.well-known/openid-configuration</c>）。</summary>
    private static async Task<OpenIdConnectConfiguration> GetConfigurationAsync(
        string authority,
        CancellationToken cancellationToken)
    {
        var key = authority.TrimEnd('/');
        var manager = new ConfigurationManager<OpenIdConnectConfiguration>(
            key + "/.well-known/openid-configuration",
            new OpenIdConnectConfigurationRetriever());
        return await manager.GetConfigurationAsync(cancellationToken);
    }

    /// <summary>
    /// 建立呼叫 IdP token 端點用的 HttpClient。
    /// 略過 SSL 憑證驗證（對齊 DAS.Authentication：私有 CA／K8s 內部環境）。
    /// </summary>
    private static HttpClient CreateHttpClient()
    {
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback =
                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
        };
        return new HttpClient(handler);
    }

    /// <summary>正規化 RedirectUri；空白時使用預設 loopback。</summary>
    private static string NormalizeRedirectUri(string redirectUri) =>
        string.IsNullOrWhiteSpace(redirectUri) ? "http://127.0.0.1:5005/" : redirectUri.Trim();

    /// <summary>HttpListener Prefix 需以 <c>/</c> 結尾。</summary>
    private static string EnsureTrailingSlash(string uri) =>
        uri.EndsWith('/') ? uri : uri + "/";

    /// <summary>自 redirect URL 查詢字串取出指定參數（已 URL-decode）。</summary>
    private static string? GetQueryValue(Uri? url, string key)
    {
        if (url is null || string.IsNullOrEmpty(url.Query))
            return null;

        foreach (var part in url.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Split('=', 2);
            if (kv.Length != 2) continue;
            if (!string.Equals(Uri.UnescapeDataString(kv[0]), key, StringComparison.OrdinalIgnoreCase))
                continue;
            return Uri.UnescapeDataString(kv[1].Replace('+', ' '));
        }

        return null;
    }
}
