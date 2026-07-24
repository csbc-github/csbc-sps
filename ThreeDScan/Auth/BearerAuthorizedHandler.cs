using System.Net.Http;
using System.Net.Http.Headers;

namespace ThreeDScan.Auth;

/// <summary>
/// HttpClient <see cref="DelegatingHandler"/>：送出請求前自動附加 <c>Authorization: Bearer</c>。
/// Token 由 <see cref="IAccessTokenSource.GetAccessTokenAsync"/> 取得（含必要時續期／重換）。
/// </summary>
public sealed class BearerAuthorizedHandler : DelegatingHandler
{
    private readonly IAccessTokenSource _tokens;

    /// <summary>以 token 來源建立 Handler（內層為預設 <see cref="HttpClientHandler"/>）。</summary>
    public BearerAuthorizedHandler(IAccessTokenSource tokens)
        : base(new HttpClientHandler())
    {
        _tokens = tokens;
    }

    /// <summary>附加 Bearer Token 後轉交內層 Handler 送出請求。</summary>
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var token = await _tokens.GetAccessTokenAsync(cancellationToken);
        if (!string.IsNullOrEmpty(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return await base.SendAsync(request, cancellationToken);
    }
}
