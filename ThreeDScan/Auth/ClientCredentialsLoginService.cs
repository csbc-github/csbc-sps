using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using DAS.Authentication.Models;
using DAS.Authentication.Providers;
using DAS.Authentication.Services;

namespace ThreeDScan.Auth;

/// <summary>
/// WPF Client Credentials 自動登入（以客戶 DAS sample 為主體）。
/// </summary>
/// <remarks>
/// 甲方 IdP 只接受 <c>client_secret_basic</c>；DAS 的 <see cref="ClientCredentialsProvider"/>
/// 只把 client_id／client_secret 放在 form body，直接使用會被回 401 invalid_client，
/// 因此改由外部注入已帶 Basic 標頭的 HttpClient。
/// </remarks>
public sealed class ClientCredentialsLoginService : IAccessTokenSource, IDisposable
{
    private readonly AuthenticationOptions _options;
    private bool _disposed;

    public ClientCredentialsLoginService(AuthenticationOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        ValidateOptions(_options);
    }

    /// <summary>
    /// 依客戶提供的 AuthenticationManager + ClientCredentialsProvider sample 執行認證。
    /// </summary>
    public async Task<bool> LoginAsync(CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        AuthDebugLog.Clear();
        AuthDebugLog.Write($"Client Credentials（DAS sample）；Authority={_options.Authority}");
        AuthDebugLog.Write($"ClientId={_options.ClientId}；Scope={ResolveScope() ?? "(未指定)"}");

        try
        {
            var manager = new AuthenticationManager();

            // 1. 建立 ClientCredentialsProvider 並注冊（HttpClient 補 Basic 認證並攔存 token）
            using var tokenHandler = new BasicAuthTokenHandler(_options.ClientId, _options.ClientSecret);
            using var httpClient = new HttpClient(tokenHandler, disposeHandler: false)
            {
                Timeout = TimeSpan.FromSeconds(30),
            };
            using var provider = new ClientCredentialsProvider(
                authority: _options.Authority.TrimEnd('/'),
                validateToken: true,
                httpClient: httpClient);
            manager.RegisterProvider("clientcredentials", provider);

            // 2. 準備請求憑證
            var request = new ClientCredentialsRequest
            {
                ClientId = _options.ClientId,
                ClientSecret = _options.ClientSecret,
                Scope = ResolveScope(),
            };

            // 3. 執行認證（向 token_endpoint 取得 Access Token）
            AuthDebugLog.Write("呼叫 AuthenticateAsync(\"clientcredentials\")…");
            var principal = await manager.AuthenticateAsync("clientcredentials", request)
                .ConfigureAwait(false);

            AuthDebugLog.Write(tokenHandler.LastStatusLine ?? "未取得 token_endpoint 回應。");

            if (principal is not null)
            {
                AuthDebugLog.Write(
                    $"認證成功；Name={principal.Identity?.Name ?? "（無 name claim）"}；expires_in={tokenHandler.ExpiresIn?.ToString() ?? "(未提供)"}");
                Debug.WriteLine($"認證成功：{principal.Identity?.Name ?? "（無 name claim）"}");

                // DAS provider 不公開 access_token 字串，改由 handler 攔存供 SignalR／API 帶 Bearer。
                if (tokenHandler.AccessToken is not { Length: > 0 } accessToken)
                {
                    AuthDebugLog.Write("認證成功但未攔存到 access_token，無法提供 Bearer。");
                    return false;
                }

                AuthSession.Current.SetTokens(
                    accessToken,
                    refreshToken: null,
                    idToken: null,
                    tokenHandler.ExpiresIn,
                    principal);
                return true;
            }

            AuthDebugLog.Write(
                tokenHandler.AccessToken is null
                    ? "認證失敗：未取得 access_token（憑證或 client 認證方式被拒）。"
                    : "認證失敗：已取得 access_token，但 validateToken 驗簽／issuer 檢查未通過。");
            Debug.WriteLine("認證失敗。");
            return false;

            #region 原自寫 token_endpoint 換票（已改用客戶 sample，暫時註解）
            /*
            AuthDebugLog.Write("取得 OIDC discovery…");
            var config = await _configurationManager
                .GetConfigurationAsync(cancellationToken)
                .ConfigureAwait(false);

            var tokenEndpoint = config.TokenEndpoint
                ?? throw new InvalidOperationException("OIDC discovery 未提供 token_endpoint。");
            AuthDebugLog.Write($"discovery 成功；token_endpoint={tokenEndpoint}");

            var fields = new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = _options.ClientId,
                ["client_secret"] = _options.ClientSecret,
            };

            var scope = ResolveScope();
            if (!string.IsNullOrWhiteSpace(scope))
                fields["scope"] = scope;

            AuthDebugLog.Write($"POST token_endpoint；ClientId={_options.ClientId}；Scope={scope ?? "(未指定)"}");
            using var response = await _httpClient
                .PostAsync(tokenEndpoint, new FormUrlEncodedContent(fields), cancellationToken)
                .ConfigureAwait(false);

            var body = await response.Content
                .ReadAsStringAsync(cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                AuthDebugLog.Write(
                    $"token_endpoint 失敗：{(int)response.StatusCode} {response.ReasonPhrase}；{ReadOAuthError(body)}");
                return false;
            }

            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            var accessToken = root.TryGetProperty("access_token", out var access)
                ? access.GetString()
                : null;
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                AuthDebugLog.Write("token_endpoint 成功，但回應缺少 access_token。");
                return false;
            }

            var expiresIn = root.TryGetProperty("expires_in", out var expires)
                && expires.TryGetInt32(out var seconds)
                    ? seconds
                    : (int?)null;

            AuthSession.Current.SetTokens(
                accessToken,
                refreshToken: null,
                idToken: null,
                expiresIn,
                BuildPrincipal(accessToken));

            AuthDebugLog.Write($"取得 Access Token 成功；expires_in={expiresIn?.ToString() ?? "(未提供)"}");
            return true;
            */
            #endregion
        }
        catch (Exception ex)
        {
            AuthDebugLog.Write($"Client Credentials 例外：{ex.GetType().Name}: {ex.Message}");
            throw;
        }
    }

    /// <inheritdoc />
    /// <remarks>Client Credentials 沒有 Refresh Token；接近過期時重新換票。</remarks>
    public async Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(AuthSession.Current.AccessToken)
            || AuthSession.Current.AccessTokenNearExpiry)
        {
            if (!await LoginAsync(cancellationToken).ConfigureAwait(false))
                return null;
        }

        return AuthSession.Current.AccessToken;
    }

    /// <summary>Scope：設定有值用設定；否則依客戶 sample 用 <c>das.api</c>。</summary>
    private string? ResolveScope()
    {
        if (!string.IsNullOrWhiteSpace(_options.ClientCredentialsScope))
            return _options.ClientCredentialsScope.Trim();

        return "das.api";
    }

    private static void ValidateOptions(AuthenticationOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Authority))
            throw new InvalidOperationException("請設定 Authentication:Authority。");
        if (string.IsNullOrWhiteSpace(options.ClientId))
            throw new InvalidOperationException("請設定 Authentication:ClientId。");
        if (string.IsNullOrWhiteSpace(options.ClientSecret))
            throw new InvalidOperationException("請設定 Authentication:ClientSecret。");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
    }

    /// <summary>
    /// 為 token 請求補上 <c>Authorization: Basic</c>，並攔存回應內容。
    /// </summary>
    private sealed class BasicAuthTokenHandler : DelegatingHandler
    {
        private readonly string _basicCredentials;

        public BasicAuthTokenHandler(string clientId, string clientSecret)
            : base(new HttpClientHandler())
        {
            _basicCredentials = Convert.ToBase64String(
                System.Text.Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}"));
        }

        /// <summary>token_endpoint 回應摘要，供診斷 Log 使用。</summary>
        public string? LastStatusLine { get; private set; }

        public string? AccessToken { get; private set; }

        public int? ExpiresIn { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", _basicCredentials);

            var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            LastStatusLine = response.IsSuccessStatusCode
                ? $"token_endpoint：{(int)response.StatusCode} {response.ReasonPhrase}"
                : $"token_endpoint 失敗：{(int)response.StatusCode} {response.ReasonPhrase}；{ReadOAuthError(body)}";

            if (response.IsSuccessStatusCode)
                ReadTokenResponse(body);

            // 內容已讀出，補回可重複讀取的內容讓 provider 繼續解析。
            var mediaType = response.Content.Headers.ContentType?.MediaType ?? "application/json";
            response.Content = new StringContent(body, System.Text.Encoding.UTF8, mediaType);
            return response;
        }

        private void ReadTokenResponse(string body)
        {
            try
            {
                using var document = JsonDocument.Parse(body);
                var root = document.RootElement;

                if (root.TryGetProperty("access_token", out var access)
                    && access.GetString() is { Length: > 0 } token)
                {
                    AccessToken = token;
                }

                if (root.TryGetProperty("expires_in", out var expires)
                    && expires.TryGetInt32(out var seconds))
                {
                    ExpiresIn = seconds;
                }
            }
            catch (JsonException)
            {
                // 交由 provider 回報格式問題。
            }
        }

        private static string ReadOAuthError(string body)
        {
            try
            {
                using var document = JsonDocument.Parse(body);
                var root = document.RootElement;
                var error = root.TryGetProperty("error", out var errorValue)
                    ? errorValue.GetString()
                    : null;
                var description = root.TryGetProperty("error_description", out var descriptionValue)
                    ? descriptionValue.GetString()
                    : null;
                return $"error={error ?? "(無)"}；description={description ?? "(無)"}";
            }
            catch (JsonException)
            {
                return "回應不是標準 OAuth JSON。";
            }
        }
    }
}
