using System.Net.Http;
using System.Net.Http.Headers;

namespace ThreeDScan.Auth;

/// <summary>
/// HttpClient <see cref="DelegatingHandler"/>：送出請求前自動附加 <c>Authorization: Bearer</c>。
/// Token 由 <see cref="OidcDesktopLoginService.GetAccessTokenAsync"/> 取得（含必要時 Refresh）。
/// </summary>
public sealed class BearerAuthorizedHandler : DelegatingHandler
{
    private readonly OidcDesktopLoginService _login;

    /// <summary>以登入服務建立 Handler（內層為預設 <see cref="HttpClientHandler"/>）。</summary>
    public BearerAuthorizedHandler(OidcDesktopLoginService login)
        : base(new HttpClientHandler())
    {
        _login = login;
    }

    /// <summary>附加 Bearer Token 後轉交內層 Handler 送出請求。</summary>
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var token = await _login.GetAccessTokenAsync(cancellationToken);
        if (!string.IsNullOrEmpty(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return await base.SendAsync(request, cancellationToken);
    }
}
