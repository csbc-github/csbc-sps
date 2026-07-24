using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http;
using System.Security.Claims;
using System.Text.Json;
using DAS.Authentication.Models;
using DAS.Authentication.Providers;
using DAS.Authentication.Services;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace ThreeDScan.Auth;

/// <summary>
/// WPF Client Credentials 全自動登入：以 client_id／secret 向 IdP 換 Access Token，寫入 <see cref="AuthSession"/>。
/// 不開瀏覽器、不需人員操作。過期時重新以 Client Credentials 換票（通常無 Refresh Token）。
/// </summary>
/// <remarks>
/// DAS <see cref="ClientCredentialsProvider"/> 只回 <see cref="ClaimsPrincipal"/>、不暴露 access_token 字串，
/// 故此處自行呼叫 token_endpoint 以保留 Bearer 字串供 Api／SignalR 使用；並以 DAS 驗證／組 principal。
/// </remarks>
public sealed class ClientCredentialsLoginService : IAccessTokenSource, IDisposable
{
    private readonly AuthenticationOptions _options;
    private readonly AuthenticationManager _authManager = new();
    private readonly ClientCredentialsProvider _provider;
    private bool _disposed;

    public ClientCredentialsLoginService(AuthenticationOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        if (string.IsNullOrWhiteSpace(_options.Authority))
            throw new InvalidOperationException("請先在 appsettings 設定 Authentication:Authority。");

        // validateToken: false — 我們已自行解析 JWT；principal 另由讀取 claims 或 DAS 組出
        _provider = new ClientCredentialsProvider(_options.Authority.TrimEnd('/'), validateToken: false, CreateHttpClient());
        _authManager.RegisterProvider("ClientCredentials", _provider);
    }

    /// <summary>
    /// 向 IdP 執行 Client Credentials，成功則寫入 <see cref="AuthSession"/>。
    /// 目前暫以客戶範例 + 診斷 Log 排查 AuthenticateAsync 卡住位置。
    /// </summary>
    public async Task<bool> LoginAsync(CancellationToken cancellationToken = default)
    {
        AuthDebugLog.Clear();
        AuthDebugLog.Write($"LoginAsync 開始；Log 檔：{AuthDebugLog.FilePath}");
        AuthDebugLog.Write($"Authority={_options.Authority}");

        // 客戶範例：AuthenticationManager + ClientCredentialsProvider
        // 加上 Logging HttpClient，才能看到卡在 discovery 還是 token_endpoint
        var authManager = new AuthenticationManager();
        var authority = string.IsNullOrWhiteSpace(_options.Authority)
            ? "https://idpapi.testk8s.csbcmis.corp"
            : _options.Authority.TrimEnd('/');

        AuthDebugLog.Write("建立 ClientCredentialsProvider（validateToken=true）…");
        var clientCredProvider = new ClientCredentialsProvider(
            authority: authority,
            validateToken: true,
            httpClient: CreateLoggingHttpClient());
        authManager.RegisterProvider("ClientCredentials", clientCredProvider);
        AuthDebugLog.Write("RegisterProvider 完成");

        var request = new ClientCredentialsRequest
        {
            ClientId = "73a05119",
            ClientSecret = "51f3197f-867d-41a6-bf65-40498af724da",
            Scope = "api1.read api1.write",
        };
        AuthDebugLog.Write($"準備 AuthenticateAsync；ClientId={request.ClientId}；Scope={request.Scope}");
        AuthDebugLog.Write("→ AuthenticateAsync 內部通常會：1) 打 /.well-known/openid-configuration  2) POST token_endpoint");

        // Provider 內部 discovery 不一定走我們傳入的 HttpClient；先自行探活並寫 Log
        try
        {
            AuthDebugLog.Write("Preflight：自行打 discovery…");
            var swDiscovery = Stopwatch.StartNew();
            var discovery = await GetConfigurationAsync(authority, cancellationToken).ConfigureAwait(false);
            AuthDebugLog.Write(
                $"Preflight discovery OK（{swDiscovery.ElapsedMilliseconds} ms）；token_endpoint={discovery.TokenEndpoint}");
        }
        catch (Exception ex)
        {
            AuthDebugLog.Write($"Preflight discovery 失敗：{ex.GetType().Name}: {ex.Message}");
            AuthDebugLog.Write("若卡在這裡／失敗，代表連不上 IdP metadata（網路／VPN／DNS／SSL）。");
            throw;
        }

        var sw = Stopwatch.StartNew();
        ClaimsPrincipal? principal;
        try
        {
            principal = await authManager.AuthenticateAsync("ClientCredentials", request)
                .ConfigureAwait(false);
            AuthDebugLog.Write($"← AuthenticateAsync 返回（{sw.ElapsedMilliseconds} ms）；principal={(principal is null ? "null" : "有值")}");
        }
        catch (Exception ex)
        {
            AuthDebugLog.Write($"← AuthenticateAsync 例外（{sw.ElapsedMilliseconds} ms）：{ex.GetType().Name}: {ex.Message}");
            if (ex.InnerException is not null)
                AuthDebugLog.Write($"  Inner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
            throw;
        }

        if (principal != null)
        {
            AuthDebugLog.Write($"驗證成功；Name={principal.Identity?.Name ?? "(無)"}；IsAuthenticated={principal.Identity?.IsAuthenticated}");
            Debug.WriteLine($"驗證成功！Identity Name: {principal.Identity?.Name}");
        }
        else
        {
            AuthDebugLog.Write("驗證失敗或 Token 無效（principal=null）。常見：token 端點 4xx、或 validateToken 驗簽失敗。");
            Debug.WriteLine("驗證失敗或 Token 無效。");
        }

        AuthDebugLog.Write("LoginAsync 結束（診斷用暫不寫入 AuthSession）");
        // 診斷階段：先確認 AuthenticateAsync 能跑完；正式流程需寫入 AuthSession.AccessToken
        return principal != null;

        // --- 原正式換票流程（需 access_token 字串）暫時註解 ---
        // ...
    }

    /// <summary>帶請求 Log + 略過私有 CA 憑證 + Timeout，避免卡死時完全無訊息。</summary>
    private static HttpClient CreateLoggingHttpClient()
    {
        var ssl = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback =
                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
        };
        var logging = new AuthLoggingHandler(ssl);
        return new HttpClient(logging)
        {
            Timeout = TimeSpan.FromSeconds(30),
        };
    }

    /// <inheritdoc />
    /// <remarks>接近過期時重新 Client Credentials 換票（無 refresh_token）。</remarks>
    public async Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(AuthSession.Current.AccessToken) || AuthSession.Current.AccessTokenNearExpiry)
        {
            if (!await LoginAsync(cancellationToken))
                return null;
        }

        return AuthSession.Current.AccessToken;
    }

    /// <summary>
    /// 以 DAS <see cref="AuthenticationManager.AuthenticateAsync"/> 走 Client Credentials（僅取 principal，供診斷）。
    /// 正式寫入 session 請用 <see cref="LoginAsync"/>（需保留 access_token 字串）。
    /// </summary>
    public Task<ClaimsPrincipal?> AuthenticatePrincipalAsync(CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        var request = new ClientCredentialsRequest
        {
            ClientId = _options.ClientId,
            ClientSecret = _options.ClientSecret,
            Scope = ResolveScope(),
        };
        return _authManager.AuthenticateAsync("ClientCredentials", request);
    }

    private async Task<ClaimsPrincipal?> BuildPrincipalAsync(string accessToken)
    {
        try
        {
            var principal = await _provider.ValidateTokenAsync(accessToken);
            if (principal?.Identity?.IsAuthenticated == true)
                return principal;
        }
        catch
        {
            // 部分 IdP 的 access_token 非 JWT 或驗簽規則不同；改為讀取 claims
        }

        var handler = new JwtSecurityTokenHandler();
        if (!handler.CanReadToken(accessToken))
            return new ClaimsPrincipal(new ClaimsIdentity("ClientCredentials"));

        var jwt = handler.ReadJwtToken(accessToken);
        var identity = new ClaimsIdentity(jwt.Claims, "ClientCredentials", "name", "role");
        return new ClaimsPrincipal(identity);
    }

    private string? ResolveScope()
    {
        if (!string.IsNullOrWhiteSpace(_options.ClientCredentialsScope))
            return _options.ClientCredentialsScope.Trim();

        // Client Credentials 通常不需 openid／offline_access；若只設了互動用 Scope，仍原樣送出由 IdP 決定
        return string.IsNullOrWhiteSpace(_options.Scope) ? null : _options.Scope.Trim();
    }

    private static async Task<OpenIdConnectConfiguration> GetConfigurationAsync(
        string authority,
        CancellationToken cancellationToken)
    {
        var key = authority.TrimEnd('/');
        var http = CreateLoggingHttpClient();
        var retriever = new HttpDocumentRetriever(http) { RequireHttps = true };
        var manager = new ConfigurationManager<OpenIdConnectConfiguration>(
            key + "/.well-known/openid-configuration",
            new OpenIdConnectConfigurationRetriever(),
            retriever);
        return await manager.GetConfigurationAsync(cancellationToken);
    }

    /// <summary>略過 SSL 憑證驗證（對齊既有 OIDC：私有 CA／K8s 內部環境）。</summary>
    private static HttpClient CreateHttpClient()
    {
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback =
                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
        };
        return new HttpClient(handler);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _provider.Dispose();
        _disposed = true;
    }
}
