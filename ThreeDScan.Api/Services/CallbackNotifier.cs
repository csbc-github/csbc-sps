using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using ThreeDScan.Application.Common;
using ThreeDScan.Application.Common.Models;
using ThreeDScan.Application.Scan.Models;

namespace ThreeDScan.Api.Services;

/// <summary>POST callbackUrl；失敗依 Callback 設定重試。</summary>
public sealed class CallbackNotifier : ICallbackNotifier
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly IHttpClientFactory _httpFactory;
    private readonly CallbackAccessTokenSource _tokens;
    private readonly CallbackSettings _settings;
    private readonly ILogger<CallbackNotifier> _logger;

    /// <summary>建立回呼發送器。</summary>
    public CallbackNotifier(
        IHttpClientFactory httpFactory,
        CallbackAccessTokenSource tokens,
        IOptions<CallbackSettings> settings,
        ILogger<CallbackNotifier> logger)
    {
        _httpFactory = httpFactory;
        _tokens = tokens;
        _settings = settings.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task NotifyAsync(string callbackUrl, TaskResultData result, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(callbackUrl))
        {
            _logger.LogWarning("callbackUrl 空白，略過回呼 taskId={TaskId}。", result.TaskId);
            return;
        }

        var delays = _settings.RetryDelaySec ?? Array.Empty<int>();
        var maxRetries = Math.Max(0, _settings.MaxRetries);
        var timeoutSec = Math.Max(1, _settings.TimeoutSec);
        var attempt = 0;
        while (true)
        {
            var ok = await TryPostOnceAsync(callbackUrl, result, timeoutSec, ct).ConfigureAwait(false);
            if (ok)
            {
                return;
            }

            if (attempt >= maxRetries)
            {
                _logger.LogWarning(
                    "callback 重試用盡 taskId={TaskId} url={Url}；結果仍可由 GET /api/tasks/{{id}}/result 查詢。",
                    result.TaskId,
                    callbackUrl);
                return;
            }

            var delaySec = 2;
            if (attempt < delays.Length && delays[attempt] > 0)
            {
                delaySec = delays[attempt];
            }
            else if (delays.Length > 0)
            {
                delaySec = Math.Max(1, delays[delays.Length - 1]);
            }

            await Task.Delay(TimeSpan.FromSeconds(delaySec), ct).ConfigureAwait(false);
            attempt++;
        }
    }

    /// <summary>單次 POST；2xx 為成功。</summary>
    private async Task<bool> TryPostOnceAsync(
        string callbackUrl,
        TaskResultData result,
        int timeoutSec,
        CancellationToken ct)
    {
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSec));
            var json = JsonSerializer.Serialize(result, JsonOptions);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var request = new HttpRequestMessage(HttpMethod.Post, callbackUrl);
            request.Content = content;
            var token = await _tokens.GetAccessTokenAsync(timeoutCts.Token).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(token))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }

            var client = _httpFactory.CreateClient("DasCallback");
            using var response = await client.SendAsync(request, timeoutCts.Token).ConfigureAwait(false);
            if ((int)response.StatusCode >= 200 && (int)response.StatusCode <= 299)
            {
                return true;
            }

            _logger.LogWarning(
                "callback HTTP {Status} taskId={TaskId} url={Url}",
                (int)response.StatusCode,
                result.TaskId,
                callbackUrl);
            return false;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "callback 送達失敗 taskId={TaskId} url={Url}", result.TaskId, callbackUrl);
            return false;
        }
    }
}
