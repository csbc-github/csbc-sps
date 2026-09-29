using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;
using ThreeDScan.Api.Auth;

namespace ThreeDScan.Api.Services;

/// <summary>用 appsettings Authentication（與 WPF 同一組 ClientId／Secret）向 IdP 換 Access Token。</summary>
public sealed class CallbackAccessTokenSource
{
    private readonly AuthenticationOptions _options;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<CallbackAccessTokenSource> _logger;
    private readonly object _gate = new();
    private string? _cachedToken;
    private DateTimeOffset _expiresAt;

    /// <summary>建立 token 來源（與 WPF 同一組 Authentication 設定）。</summary>
    public CallbackAccessTokenSource(
        IOptions<AuthenticationOptions> options,
        IHttpClientFactory httpFactory,
        ILogger<CallbackAccessTokenSource> logger)
    {
        _options = options.Value;
        _httpFactory = httpFactory;
        _logger = logger;
    }

    /// <summary>憑證不齊時回 null，callback 就不帶 Authorization。</summary>
    public async Task<string?> GetAccessTokenAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_options.Authority)
            || string.IsNullOrWhiteSpace(_options.ClientId)
            || string.IsNullOrWhiteSpace(_options.ClientSecret))
        {
            return null;
        }

        lock (_gate)
        {
            if (!string.IsNullOrWhiteSpace(_cachedToken) && _expiresAt > DateTimeOffset.Now.AddSeconds(30))
            {
                return _cachedToken;
            }
        }

        try
        {
            var client = _httpFactory.CreateClient("DasCallback");
            var discoveryUrl = _options.Authority.TrimEnd('/') + "/.well-known/openid-configuration";
            using var discovery = await client.GetAsync(discoveryUrl, ct).ConfigureAwait(false);
            discovery.EnsureSuccessStatusCode();
            using var discoveryStream = await discovery.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var discoveryDoc = await JsonDocument.ParseAsync(discoveryStream, cancellationToken: ct)
                .ConfigureAwait(false);
            if (!discoveryDoc.RootElement.TryGetProperty("token_endpoint", out var tokenEndpointEl))
            {
                _logger.LogWarning("IdP discovery 沒有 token_endpoint，callback 不帶 Bearer。");
                return null;
            }

            var tokenEndpoint = tokenEndpointEl.GetString();
            if (string.IsNullOrWhiteSpace(tokenEndpoint))
            {
                return null;
            }

            var basic = Convert.ToBase64String(
                System.Text.Encoding.ASCII.GetBytes(_options.ClientId + ":" + _options.ClientSecret));
            using var tokenRequest = new HttpRequestMessage(HttpMethod.Post, tokenEndpoint);
            tokenRequest.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
            tokenRequest.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
            });

            using var tokenResponse = await client.SendAsync(tokenRequest, ct).ConfigureAwait(false);
            var body = await tokenResponse.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!tokenResponse.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "換發 callback token 失敗 HTTP {Status}：{Body}",
                    (int)tokenResponse.StatusCode,
                    body);
                return null;
            }

            using var tokenDoc = JsonDocument.Parse(body);
            if (!tokenDoc.RootElement.TryGetProperty("access_token", out var accessEl))
            {
                return null;
            }

            var access = accessEl.GetString();
            var expiresIn = 300;
            if (tokenDoc.RootElement.TryGetProperty("expires_in", out var expEl) && expEl.TryGetInt32(out var sec))
            {
                expiresIn = sec;
            }

            lock (_gate)
            {
                _cachedToken = access;
                _expiresAt = DateTimeOffset.Now.AddSeconds(expiresIn);
            }

            return access;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "換發 callback token 失敗。");
            return null;
        }
    }
}
