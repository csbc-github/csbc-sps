using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using ThreeDScan.Api.Auth;
using ThreeDScan.Application.Common;
using ThreeDScan.Application.Common.Models;
using ThreeDScan.Domain.Models;
using ThreeDScan.Domain.Repositories;

namespace ThreeDScan.Api.Services;

/// <summary>
/// 終態任務 POST callbackUrl。對應 Hank <c>Scan3D.Service.CallbackWorker</c>。
/// </summary>
/// <remarks>憑證已設定但換不到 token 時當次失敗並重試，不帶空 Authorization 送出。</remarks>
public sealed class ScanCallbackWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly IHttpClientFactory _httpFactory;
    private readonly CallbackAccessTokenSource _tokens;
    private readonly AuthenticationOptions _auth;
    private readonly CallbackSettings _settings;
    private readonly ISystemRuntimeConfig _runtime;
    private readonly ILogger<ScanCallbackWorker> _logger;

    /// <summary>建立回呼背景服務。</summary>
    public ScanCallbackWorker(
        IServiceScopeFactory scopes,
        IHttpClientFactory httpFactory,
        CallbackAccessTokenSource tokens,
        IOptions<AuthenticationOptions> auth,
        IOptions<CallbackSettings> settings,
        ISystemRuntimeConfig runtime,
        ILogger<ScanCallbackWorker> logger)
    {
        _scopes = scopes;
        _httpFactory = httpFactory;
        _tokens = tokens;
        _auth = auth.Value;
        _settings = settings.Value;
        _runtime = runtime;
        _logger = logger;
    }

    /// <inheritdoc />
    /// <remarks>來自 Hank Scan3D 的 <c>Scan3D.Service.CallbackWorker</c>。</remarks>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.Mode.Equals("Enabled", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SweepAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Callback 巡檢失敗。");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>找出到期應送的終態任務並送出。</summary>
    /// <remarks>來自 Hank Scan3D 的 <c>Scan3D.Service.CallbackWorker</c>。</remarks>
    private async Task SweepAsync(CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IScanTaskRepository>();
        var ids = await store.TaskIdsAsync(ct).ConfigureAwait(false);
        var maxAttempts = Math.Max(1, _settings.MaxRetries) + 1;
        foreach (var id in ids)
        {
            if (ct.IsCancellationRequested)
            {
                return;
            }

            try
            {
                var task = await store.FindAsync(id, ct).ConfigureAwait(false);
                if (task is null
                    || !task.Terminal
                    || task.CallbackUrl is null
                    || task.CallbackDelivered
                    || task.CallbackAttempts >= maxAttempts
                    || task.CallbackNextAttemptAt > DateTimeOffset.UtcNow)
                {
                    continue;
                }

                if (task.CallbackAttempts > 0 && task.CallbackNextAttemptAt is null)
                {
                    continue;
                }

                await DeliverAsync(store, task, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Callback bookkeeping failed for {TaskId}", id);
            }
        }
    }

    /// <summary>先記次數再 POST；失敗寫入下次時間。</summary>
    /// <remarks>來自 Hank Scan3D 的 <c>Scan3D.Service.CallbackWorker</c>。</remarks>
    private async Task DeliverAsync(IScanTaskRepository store, TaskDocument task, CancellationToken stoppingToken)
    {
        var delays = _settings.RetryDelaySec is { Length: > 0 } ? _settings.RetryDelaySec : new[] { 2, 4, 8, 16, 32 };
        var maxRetries = Math.Max(0, _settings.MaxRetries);
        var attempt = task.CallbackAttempts + 1;
        await store.UpdateAsync(task.TaskId, d =>
        {
            d.CallbackAttempts = attempt;
            d.CallbackNextAttemptAt = attempt <= maxRetries
                ? DateTimeOffset.UtcNow.AddSeconds(DelayAt(delays, attempt - 1))
                : null;
        }, stoppingToken).ConfigureAwait(false);

        string? failure = null;
        try
        {
            if (!Uri.TryCreate(task.CallbackUrl, UriKind.Absolute, out var uri)
                || uri.Scheme is not ("http" or "https"))
            {
                throw new InvalidOperationException("Callback URL 必須是 http 或 https。");
            }

            if (!CallbackHostPolicy.IsHostAllowed(_settings.Mode, uri.Host, _runtime.CallbackAllowedHosts))
            {
                throw new InvalidOperationException("Callback host is not configured in CallbackAllowedHosts.");
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, _settings.TimeoutSec)));
            var envelope = store.Envelope(task);
            var json = JsonSerializer.Serialize(envelope, JsonDefaults.Options);
            using var request = new HttpRequestMessage(HttpMethod.Post, uri);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");

            if (TokenRequired())
            {
                var token = await _tokens.GetAccessTokenAsync(timeout.Token).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(token))
                {
                    throw new InvalidOperationException("無法取得 callback Access Token。");
                }

                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }

            var client = _httpFactory.CreateClient("DasCallback");
            using var response = await client.SendAsync(request, timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                failure = $"HTTP {(int)response.StatusCode}";
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
        {
            failure = ex.Message;
        }

        await store.UpdateAsync(task.TaskId, d =>
        {
            d.CallbackDelivered = failure is null;
            d.CallbackLastError = failure;
            if (failure is null)
            {
                d.CallbackNextAttemptAt = null;
            }
            else if (attempt <= maxRetries)
            {
                d.CallbackNextAttemptAt = DateTimeOffset.UtcNow.AddSeconds(DelayAt(delays, attempt - 1));
            }
        }, stoppingToken).ConfigureAwait(false);
    }

    /// <summary>IdP 憑證齊全時必須帶 Bearer。</summary>
    private bool TokenRequired()
    {
        return !string.IsNullOrWhiteSpace(_auth.Authority)
            && !string.IsNullOrWhiteSpace(_auth.ClientId)
            && !string.IsNullOrWhiteSpace(_auth.ClientSecret);
    }

    /// <summary>指數退避秒數。</summary>
    private static int DelayAt(int[] delays, int index)
    {
        if (index >= 0 && index < delays.Length && delays[index] > 0)
        {
            return delays[index];
        }

        return delays.Length > 0 ? Math.Max(1, delays[delays.Length - 1]) : 2;
    }
}
