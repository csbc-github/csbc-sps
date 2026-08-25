using System.Net.Http;

namespace ThreeDScan.Auth;

/// <summary>建立已掛 <see cref="BearerAuthorizedHandler"/> 的 <see cref="HttpClient"/>，便於用 WPF 取得的 Token 呼叫 Api。</summary>
public static class AuthorizedHttpClientFactory
{
    /// <summary>
    /// 建立會自動帶 Bearer 的 HttpClient。
    /// <paramref name="baseAddress"/> 例：<c>http://localhost:5295/</c>。
    /// </summary>
    public static HttpClient Create(IAccessTokenSource tokens, string? baseAddress = null)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        var client = new HttpClient(new BearerAuthorizedHandler(tokens), disposeHandler: true);
        if (!string.IsNullOrWhiteSpace(baseAddress))
            client.BaseAddress = new Uri(baseAddress.EndsWith('/') ? baseAddress : baseAddress + "/");
        return client;
    }

    /// <summary>依 Authentication 設定建立 token 來源與 HttpClient。</summary>
    public static HttpClient Create(AuthenticationOptions options, string? baseAddress = null)
    {
        return Create(AuthTokenServiceFactory.Create(options), baseAddress);
    }
}
