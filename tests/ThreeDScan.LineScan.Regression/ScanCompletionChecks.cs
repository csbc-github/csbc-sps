using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ThreeDScan.Api.Auth;
using ThreeDScan.Api.Services;
using ThreeDScan.Application.Common;
using ThreeDScan.Application.Common.Models;
using ThreeDScan.Application.Scan;
using ThreeDScan.Application.Scan.Models;
using ThreeDScan.Domain.Abstractions;
using ThreeDScan.Domain.Models;
using ThreeDScan.Domain.Repositories;
using ThreeDScan.Infrastructure.LineScan;
using ThreeDScan.Infrastructure.LineScan.Hank;

namespace ThreeDScan.LineScan.Regression;

internal static class ScanCompletionChecks
{
    internal static async Task RunAsync()
    {
        Exception measurementFailure;
        try
        {
            new LineScanMeasureService().MeasureLine(Array.Empty<IReadOnlyList<Vertex>>(),
                new Calibration(new P(0, 0), new P(100, 0)), CancellationToken.None);
            throw new InvalidOperationException("An empty measurement should fail.");
        }
        catch (InvalidDataException error) { measurementFailure = error; }

        await VerifyAsync(measurementFailure, callbackFailure: false);
        await VerifyAsync(measurementFailure, callbackFailure: true);
        await VerifyAsync(new FormatException("Unexpected recorded-data format failure"), callbackFailure: false);
        await VerifyAsync(failure: null, callbackFailure: true);
        await VerifyAsync(failure: null, callbackFailure: false, abortDuringMeasurement: true);
        await VerifyAsync(measurementFailure, callbackFailure: false, abortDuringMeasurement: true);
        Console.WriteLine("PASS accepted scans finish independently of durable callback delivery, retries and polling-only mode");
    }

    static async Task VerifyAsync(Exception? failure, bool callbackFailure, bool abortDuringMeasurement = false)
    {
        const string taskId = "scan-completion-regression";
        const string callbackUrl = "http://127.0.0.1/unused-test-callback";
        var document = new TaskDocument
        {
            TaskId = taskId, Kind = "Line", ScannerId = "regression", Status = "Processing",
            Phase = "LineAcquiring", TraceId = "completion-trace", RollerSpeed = 100
        };
        int stopped = 0, measured = 0, released = 0;
        int directNotifications = 0;
        var queue = new AcceptedWorkQueue();
        var cache = new DasTaskResultCache();
        cache.SetLatestLine(new TaskResultData { TaskId = taskId, Status = "Processing" });
        var repository = InterfaceDouble.Create<IScanTaskRepository>((method, args) => method.Name switch
        {
            nameof(IScanTaskRepository.GetAsync) => Task.FromResult(document),
            nameof(IScanTaskRepository.FindAsync) => Task.FromResult<TaskDocument?>(document),
            nameof(IScanTaskRepository.TaskIdsAsync) => Task.FromResult<IReadOnlyList<string>>([taskId]),
            nameof(IScanTaskRepository.Envelope) => new TaskEnvelope
            {
                TaskId = document.TaskId, Status = document.Status, TraceId = document.TraceId, Phase = document.Phase,
                Result = document.Status == "Completed" ? document.Result : null,
                Error = document.Status == "Failed" ? document.Error : null
            },
            nameof(IScanTaskRepository.TaskDirectory) => Path.Combine(Path.GetTempPath(), taskId),
            nameof(IScanTaskRepository.UpdateAsync) => Mutate((Action<TaskDocument>)args![1]!),
            _ => throw new NotSupportedException(method.Name)
        });
        Task<TaskDocument> Mutate(Action<TaskDocument> mutation) { mutation(document); return Task.FromResult(document); }
        var devices = InterfaceDouble.Create<IDeviceCoordinator>((method, _) =>
        {
            if (method.Name != nameof(IDeviceCoordinator.ReleaseByTaskId)) throw new NotSupportedException(method.Name);
            released++;
            return null;
        });
        var scanner = InterfaceDouble.Create<ISickLineScanner>((method, _) =>
        {
            if (method.Name == nameof(ISickLineScanner.StopRecordingAsync)) { stopped++; return Task.CompletedTask; }
            if (method.Name == nameof(ISickLineScanner.MeasureAsync))
            {
                measured++;
                if (abortDuringMeasurement)
                {
                    document.Status = "Aborted";
                    document.Phase = "Aborted";
                    cache.SetLatestLine(new TaskResultData { TaskId = taskId, Status = "Aborted" });
                    released++;
                }
                return failure is null
                    ? Task.FromResult<IReadOnlyList<MeasuredAngleIron>>([new MeasuredAngleIron { Index = 1, Length = 100 }])
                    : Task.FromException<IReadOnlyList<MeasuredAngleIron>>(failure);
            }
            throw new NotSupportedException(method.Name);
        });
        var baseline = InterfaceDouble.Create<IRollerCalibrationRepository>((method, _) =>
            method.Name == nameof(IRollerCalibrationRepository.GetAsync)
                ? Task.FromResult<RollerBaselineRecord?>(new RollerBaselineRecord { ScannerId = "regression" })
                : throw new NotSupportedException(method.Name));
        var blockedNotification = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var callback = InterfaceDouble.Create<ICallbackNotifier>((method, _) =>
        {
            if (method.Name != nameof(ICallbackNotifier.NotifyAsync)) throw new NotSupportedException(method.Name);
            directNotifications++;
            return blockedNotification.Task;
        });
        var orchestrator = InterfaceDouble.Create<IScanOrchestrator>((method, _) =>
            method.Name == nameof(IScanOrchestrator.FindAsync) ? Task.FromResult<TaskDocument?>(document) : throw new NotSupportedException(method.Name));
        var services = new ServiceCollection();
        services.AddSingleton(repository);
        services.AddScoped<IScanAppService>(scope => new ScanAppService(
            registry: null!, captureFactory: null!, pipeline: null!, taskService: null!, storage: null!, jobThickness: null!, scanCallLog: null!,
            taskResults: cache, workQueue: queue, callback: callback, scopeFactory: scope.GetRequiredService<IServiceScopeFactory>(),
            surfaceScan: Options.Create(new SurfaceScanSettings()), lineScanners: null!, scanTaskStore: repository,
            lineScannerSettings: null!, devices: devices, lineDevice: null!, lineScanner: scanner, rollerCalibration: baseline,
            lineScan: Options.Create(new LineScanSettings()), orchestrator: orchestrator, logger: NullLogger<ScanAppService>.Instance));
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var application = scope.ServiceProvider.GetRequiredService<IScanAppService>();
        var accepted = await application.CompleteLineScanAsync(new LineScanCompleteRequest { TaskId = taskId, CallbackUrl = callbackUrl });
        Program.Check(accepted.Success && document.Phase == "LineProcessing", "completion is accepted through the real work queue");
        var nextJob = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        queue.Enqueue((_, _) => { nextJob.TrySetResult(); return Task.CompletedTask; });
        using (var consumer = new AcceptedWorkHostedService(queue, provider, NullLogger<AcceptedWorkHostedService>.Instance))
        {
            await consumer.StartAsync(CancellationToken.None);
            try { await nextJob.Task.WaitAsync(TimeSpan.FromSeconds(5)); }
            finally
            {
                blockedNotification.TrySetResult();
                await consumer.StopAsync(CancellationToken.None);
            }
        }
        Program.Check(directNotifications == 0,
            "single accepted-work consumer reaches the next job without invoking or waiting for network notifications");

        string expectedStatus = abortDuringMeasurement ? "Aborted" : failure is null ? "Completed" : "Failed";
        Program.Check(document.Terminal && document.Status == expectedStatus && document.Phase == expectedStatus,
            "accepted completion persists the correct terminal state before callback delivery");
        Program.Check(stopped == 1 && measured == 1 && released == 1,
            "accepted completion stops, measures and releases the scanner exactly once");
        if (abortDuringMeasurement)
        {
            Program.Check(document.Result is null && document.Error is null,
                "late measurement completion or error never overwrites an aborted task");
        }
        else if (failure is null)
        {
            Program.Check(document.Error is null && document.Result?.GetProperty("detectedCount").GetInt32() == 1,
                "successful measurement is persisted before callback delivery");
        }
        else
        {
            Program.Check(document.Result is null && document.Error?.Code == "SCAN_LINE_002" && document.Error.Message == failure.Message,
                "measurement failure retains its original message with no fabricated result");
        }
        var latest = application.GetLatestLineScanResult();
        var queried = await application.GetTaskResultAsync(taskId);
        Program.Check(latest.Status == expectedStatus && queried.Data?.Status == expectedStatus,
            "latest and task result polling both leave Processing");
        Program.Check(document.CallbackUrl == callbackUrl && document.CallbackAttempts == 0 && !document.CallbackDelivered,
            "completion retains pending callback metadata for the independent worker");
        await VerifyCallbackWorkerAsync(provider, document, callbackFailure);
    }

    static async Task VerifyCallbackWorkerAsync(ServiceProvider provider, TaskDocument document, bool firstAttemptFails)
    {
        using var handler = new CallbackHandler(firstAttemptFails);
        using var client = new HttpClient(handler);
        var httpFactory = InterfaceDouble.Create<IHttpClientFactory>((method, _) =>
            method.Name == nameof(IHttpClientFactory.CreateClient) ? client : throw new NotSupportedException(method.Name));
        var runtime = InterfaceDouble.Create<ISystemRuntimeConfig>((method, _) =>
            method.Name == "get_CallbackAllowedHosts" ? new[] { "127.0.0.1" } : throw new NotSupportedException(method.Name));
        var auth = Options.Create(new AuthenticationOptions());
        var tokens = new CallbackAccessTokenSource(auth, httpFactory, NullLogger<CallbackAccessTokenSource>.Instance);
        var scopes = provider.GetRequiredService<IServiceScopeFactory>();
        ScanCallbackWorker CreateWorker(string mode) => new(scopes, httpFactory, tokens, auth,
            Options.Create(new CallbackSettings { Mode = mode, MaxRetries = 1, RetryDelaySec = [1], TimeoutSec = 1 }),
            runtime, NullLogger<ScanCallbackWorker>.Instance);

        using (var pollingOnly = CreateWorker("PollingOnly"))
        {
            await pollingOnly.StartAsync(CancellationToken.None);
            try { await pollingOnly.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(5)); }
            finally { await pollingOnly.StopAsync(CancellationToken.None); }
        }
        Program.Check(handler.Payloads.Count == 0 && document.CallbackAttempts == 0,
            "PollingOnly disables the actual callback worker without attempting HTTP");

        using var worker = CreateWorker("Enabled");
        var sweep = typeof(ScanCallbackWorker).GetMethod("SweepAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Task SweepAsync() => (Task)sweep.Invoke(worker, [CancellationToken.None])!;
        string status = document.Status;
        string? result = document.Result?.GetRawText();
        string? error = document.Error?.Message;
        await SweepAsync();
        Program.Check(document.CallbackAttempts == 1 && handler.Payloads.Count == 1,
            "independent callback worker persists one attempt for the terminal scan");
        if (firstAttemptFails)
        {
            Program.Check(!document.CallbackDelivered && document.CallbackNextAttemptAt > DateTimeOffset.Now
                && document.CallbackLastError == "HTTP 503", "failed callback persists its error and next retry time");
            await SweepAsync();
            Program.Check(handler.Payloads.Count == 1, "pending retry is not delivered before its persisted due time");
            document.CallbackNextAttemptAt = DateTimeOffset.Now.AddSeconds(-1);
            await SweepAsync();
            Program.Check(document.CallbackAttempts == 2 && handler.Payloads.Count == 2,
                "due callback retry uses the persisted terminal scan");
        }
        Program.Check(document.CallbackDelivered && document.CallbackNextAttemptAt is null && document.CallbackLastError is null,
            "successful callback persists delivery and clears retry metadata");
        int delivered = handler.Payloads.Count;
        await SweepAsync();
        Program.Check(handler.Payloads.Count == delivered, "delivered task is not notified again on the next sweep");
        Program.Check(document.Status == status && document.Result?.GetRawText() == result && document.Error?.Message == error,
            "callback delivery and retry bookkeeping preserve the persisted scan result");
        foreach (string body in handler.Payloads)
        {
            using var json = JsonDocument.Parse(body);
            var payload = json.RootElement;
            Program.Check(payload.GetProperty("taskId").GetString() == document.TaskId
                && payload.GetProperty("status").GetString() == status && payload.GetProperty("traceId").GetString() == document.TraceId,
                "durable callback preserves the task, status and trace JSON contract");
            Program.Check(status == "Completed"
                ? payload.GetProperty("result").GetProperty("detectedCount").GetInt32() == 1 && payload.GetProperty("error").ValueKind == JsonValueKind.Null
                : payload.GetProperty("result").ValueKind == JsonValueKind.Null && (status != "Failed"
                    || payload.GetProperty("error").GetProperty("message").GetString() == error),
                "durable callback preserves the result and error JSON contract");
        }
    }

    sealed class CallbackHandler(bool firstAttemptFails) : HttpMessageHandler
    {
        internal List<string> Payloads { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Program.Check(request.RequestUri?.AbsoluteUri == "http://127.0.0.1/unused-test-callback",
                "callback HTTP handler receives the persisted destination without opening a socket");
            Payloads.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            return new HttpResponseMessage(firstAttemptFails && Payloads.Count == 1 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK);
        }
    }
}
