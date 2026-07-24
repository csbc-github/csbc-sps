using System.Diagnostics;
using System.Net.Http;

namespace ThreeDScan.Auth;

/// <summary>記錄每一筆 HTTP 請求／回應，用來確認 AuthenticateAsync 卡在哪個 URL。</summary>
internal sealed class AuthLoggingHandler : DelegatingHandler
{
    public AuthLoggingHandler(HttpMessageHandler inner)
        : base(inner)
    {
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        AuthDebugLog.Write($"HTTP → {request.Method} {request.RequestUri}");
        var sw = Stopwatch.StartNew();
        try
        {
            var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            AuthDebugLog.Write(
                $"HTTP ← {(int)response.StatusCode} {response.ReasonPhrase} ({sw.ElapsedMilliseconds} ms) {request.RequestUri}");
            return response;
        }
        catch (Exception ex)
        {
            AuthDebugLog.Write(
                $"HTTP ✗ {ex.GetType().Name}: {ex.Message} ({sw.ElapsedMilliseconds} ms) {request.RequestUri}");
            throw;
        }
    }
}
