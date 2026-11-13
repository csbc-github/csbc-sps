using System.IO;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ThreeDScan.Api.Services;
using ThreeDScan.Application.Common;
using ThreeDScan.Application.Common.Models;
using ThreeDScan.Application.Scan;
using ThreeDScan.Application.Scan.Models;
using ThreeDScan.Domain.Abstractions;
using ThreeDScan.Domain.Models;
using ThreeDScan.Domain.Repositories;
using ThreeDScan.Infrastructure.Configuration;
using ThreeDScan.Infrastructure.LineScan;
using ThreeDScan.Infrastructure.LineScan.Hank;

namespace ThreeDScan.LineScan.Regression;

internal static class NoMotionChecks
{
    internal static async Task RunAsync()
    {
        string directory = Path.Combine(Path.GetTempPath(), "ThreeDScanNoMotionRegression_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(Program.Fixtures, "measurement_639301351780719706.json")));
            var root = fixture.RootElement;
            P Read(JsonElement value) => new(value[0].GetDouble(), value[1].GetDouble());
            var saved = root.GetProperty("calibration");
            var calibration = new Calibration(Read(saved.GetProperty("first")), Read(saved.GetProperty("second")));
            var settings = new ReconstructionOptions
            {
                PositionSource = "encoder", AverageScans = 1, MinHeight = 3,
                RoiXY = root.GetProperty("roi").EnumerateArray().Select(value => value.GetDouble()).ToArray()
            };
            var session = EncoderScanSession.Start(Path.Combine(directory, "line_raw.ply"), calibration, settings);
            var points = root.GetProperty("points").EnumerateArray().Select(value => (P?)Read(value)).ToArray();
            for (ushort frame = 0; frame < 2; frame++)
                session.Submit(new Scan(frame, (uint)(1000 + frame * 1000), 600, 0, points)
                {
                    Encoders = [new EncoderReading(0, 0)]
                });
            Program.Check(session.Progress.Samples == 2 && session.Progress.SpanMm == 0,
                "zero-encoder fixture contains valid frames but no measured travel");
            Exception? guard = null;
            try { await session.StopAndBuildAsync(); }
            catch (ArgumentException error) { guard = error; }
            Program.Check(guard != null && guard.Message.Contains("Encoder") && guard.Message.Contains("位置變化"),
                "real encoder reconstruction refuses zero displacement");
            Program.Check(File.Exists(session.RawPath) && File.Exists(Path.ChangeExtension(session.RawPath, ".encoder.jsonl")),
                "zero-motion raw recording and encoder evidence are retained");
            Program.Check(!File.Exists(session.OutputPath) && !File.Exists(Path.ChangeExtension(session.OutputPath, ".json")),
                "zero-motion guard never invents a reconstructed Y or successful 3D artifact");

            await VerifyAcceptedFailureAsync(directory, session.StopAndBuildAsync, guard!, simulateCallbackFailure: false);
            using var configurationServices = new ServiceCollection().BuildServiceProvider();
            var registry = Options.Create(new LineScannerRegistryOptions());
            var resolver = new LineScanOptionsResolver(registry, configurationServices.GetRequiredService<IServiceScopeFactory>());
            var direct = InjectOfflineCapture(directory, session, calibration, settings, resolver, registry);
            ScanTaskException? translated = null;
            try { await direct.Adapter.StopRecordingAsync(directory); }
            catch (ScanTaskException error) { translated = error; }
            Program.Check(translated?.StatusCode == 400 && translated.Code == "SCAN_LINE_MEASUREMENT"
                && translated.Message.Contains(guard!.Message), "actual host/adapter translates no-motion failure to domain task exception");
            Program.Check(direct.Captures.Count == 0 && direct.Endpoints.Count == 0,
                "actual host removes failed capture and releases endpoint reservation");
            foreach (bool callbackFailure in new[] { false, true })
            {
                var actual = InjectOfflineCapture(directory, session, calibration, settings, resolver, registry);
                async Task<Cloud> CompleteThroughAdapter()
                {
                    await actual.Adapter.StopRecordingAsync(directory);
                    throw new InvalidOperationException("A zero-motion capture must never complete successfully.");
                }
                await VerifyAcceptedFailureAsync(directory, CompleteThroughAdapter, guard!, callbackFailure);
                Program.Check(actual.Captures.Count == 0 && actual.Endpoints.Count == 0,
                    "queued actual host/adapter failure also releases capture and endpoint");
            }
        }
        finally
        {
            string resolved = Path.GetFullPath(directory);
            string parent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar);
            if (!string.Equals(Path.GetDirectoryName(resolved), parent, StringComparison.OrdinalIgnoreCase)
                || !Path.GetFileName(resolved).StartsWith("ThreeDScanNoMotionRegression_", StringComparison.Ordinal))
                throw new InvalidOperationException("Refusing to delete outside the owned no-motion temp directory.");
            Directory.Delete(resolved, recursive: true);
        }
        Console.WriteLine("PASS zero encoder motion remains rejected and accepted completion reaches persisted Failed state");
    }

    static async Task VerifyAcceptedFailureAsync(string directory, Func<Task<Cloud>> stop, Exception expectedFailure, bool simulateCallbackFailure)
    {
        string taskId = Path.GetFileName(directory);
        const string callbackUrl = "http://127.0.0.1/unused-test-callback";
        var document = new TaskDocument
        {
            TaskId = taskId, Kind = "Line", ScannerId = "regression", Status = "Processing",
            Phase = "LineAcquiring", TraceId = "no-motion-trace"
        };
        int released = 0, stopped = 0, measured = 0;
        var callbackPayloads = new List<TaskResultData>();
        var queue = new AcceptedWorkQueue();
        var cache = new DasTaskResultCache();
        cache.SetLatestLine(new TaskResultData { TaskId = taskId, Status = "Processing" });
        var repository = InterfaceDouble.Create<IScanTaskRepository>((method, args) => method.Name switch
        {
            nameof(IScanTaskRepository.GetAsync) => Task.FromResult(document),
            nameof(IScanTaskRepository.FindAsync) => Task.FromResult<TaskDocument?>(document),
            nameof(IScanTaskRepository.TaskDirectory) => directory,
            nameof(IScanTaskRepository.UpdateAsync) => Mutate((Action<TaskDocument>)args![1]!),
            _ => throw new NotSupportedException("Unexpected repository operation: " + method.Name)
        });
        Task<TaskDocument> Mutate(Action<TaskDocument> mutation) { mutation(document); return Task.FromResult(document); }
        var devices = InterfaceDouble.Create<IDeviceCoordinator>((method, _) =>
        {
            if (method.Name != nameof(IDeviceCoordinator.ReleaseByTaskId)) throw new NotSupportedException(method.Name);
            released++; return null;
        });
        var scanner = InterfaceDouble.Create<ISickLineScanner>((method, _) =>
        {
            if (method.Name == nameof(ISickLineScanner.StopRecordingAsync)) { stopped++; return stop(); }
            if (method.Name == nameof(ISickLineScanner.MeasureAsync)) measured++;
            throw new NotSupportedException("No-motion completion must not measure or start a scanner: " + method.Name);
        });
        var callback = InterfaceDouble.Create<ICallbackNotifier>((method, args) =>
        {
            if (method.Name != nameof(ICallbackNotifier.NotifyAsync)) throw new NotSupportedException(method.Name);
            Program.Check((string)args![0]! == callbackUrl, "failure callback uses accepted task callback metadata");
            callbackPayloads.Add((TaskResultData)args[1]!);
            return simulateCallbackFailure
                ? Task.FromException(new IOException("Simulated callback delivery failure; no network request was sent"))
                : Task.CompletedTask;
        });
        var orchestrator = InterfaceDouble.Create<IScanOrchestrator>((method, _) =>
            method.Name == nameof(IScanOrchestrator.FindAsync) ? Task.FromResult<TaskDocument?>(document) : throw new NotSupportedException(method.Name));
        var services = new ServiceCollection();
        services.AddScoped<IScanAppService>(scope => new ScanAppService(
            registry: null!, captureFactory: null!, pipeline: null!, taskService: null!, storage: null!, jobThickness: null!, scanCallLog: null!,
            taskResults: cache, workQueue: queue, callback: callback, scopeFactory: scope.GetRequiredService<IServiceScopeFactory>(),
            surfaceScan: Options.Create(new SurfaceScanSettings()), lineScanners: null!, scanTaskStore: repository,
            lineScannerSettings: null!, devices: devices, lineDevice: null!, lineScanner: scanner, rollerCalibration: null!,
            lineScan: Options.Create(new LineScanSettings()), orchestrator: orchestrator, logger: NullLogger<ScanAppService>.Instance));
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var application = scope.ServiceProvider.GetRequiredService<IScanAppService>();
        var accepted = await application.CompleteLineScanAsync(new LineScanCompleteRequest { TaskId = taskId, CallbackUrl = callbackUrl });
        Program.Check(accepted.Success && document.Phase == "LineProcessing", "actual complete accepts task and queues processing");
        var work = await queue.DequeueAsync(CancellationToken.None);
        Exception? escaped = null;
        try { await work(provider, CancellationToken.None); }
        catch (Exception error) { escaped = error; }
        Console.WriteLine($"No-motion worker: escaped={escaped?.GetType().Name ?? "none"}; status={document.Status}; phase={document.Phase}; callbackFailure={simulateCallbackFailure}");
        Program.Check(escaped == null || simulateCallbackFailure && escaped is IOException,
            "expected no-motion data failure is contained by application completion boundary");
        Program.Check(document.Terminal && document.Status == "Failed" && document.Phase == "Failed",
            "zero-motion accepted task reaches Failed instead of remaining Processing");
        Program.Check(document.Result == null && document.Error?.Code == "SCAN_LINE_002" && document.Error.Message.Contains(expectedFailure.Message),
            "zero-motion task retains failed measurement error without success result");
        Program.Check(stopped == 1 && measured == 0, "failure stops recording once and never fabricates a measurement");
        Program.Check(released == 1, "failed line task releases device reservation");
        var latest = application.GetLatestLineScanResult();
        Program.Check(latest.Status == "Failed" && latest.Error?.Code == "SCAN_LINE_002", "latest line cache leaves Processing on expected failure");
        var queried = await application.GetTaskResultAsync(taskId);
        Program.Check(queried.Data?.Status == "Failed" && queried.Data.Error?.Code == "SCAN_LINE_002", "actual task query preserves terminal failure");
        Program.Check(callbackPayloads.Count == 1 && callbackPayloads[0].Status == "Failed" && callbackPayloads[0].Result == null
            && callbackPayloads[0].TraceId == document.TraceId, "callback receives matching failed payload and trace metadata");
    }

    sealed record OfflineCapture(SickCSharpLineScanner Adapter, System.Collections.IDictionary Captures,
        Dictionary<string, string> Endpoints);

    static OfflineCapture InjectOfflineCapture(string directory, EncoderScanSession session, Calibration calibration,
        ReconstructionOptions settings, LineScanOptionsResolver resolver, IOptions<LineScannerRegistryOptions> registry)
    {
        // Inject only an already-recorded capture. The real lease owns an unstarted Scanner, so no
        // socket is opened, no hardware is contacted, and FinalizeCore/adapter cleanup run unchanged.
        string taskId = Path.GetFileName(directory);
        var host = new LineScanDeviceHost(resolver);
        var lease = new LineScanLease(new Scanner(), ownsScanner: true);
        var captureType = typeof(LineScanDeviceHost).GetNestedType("Capture", BindingFlags.NonPublic)!;
        var capture = captureType.GetConstructors()[0].Invoke([taskId, "regression", "offline-test-endpoint",
            calibration, settings, lease, session]);
        var captures = (System.Collections.IDictionary)typeof(LineScanDeviceHost)
            .GetField("_captures", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host)!;
        var endpoints = (Dictionary<string, string>)typeof(LineScanDeviceHost)
            .GetField("_endpoints", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host)!;
        captures.Add(taskId, capture);
        endpoints.Add("offline-test-endpoint", taskId);
        return new(new SickCSharpLineScanner(host, resolver, new LineScanMeasureService(), registry), captures, endpoints);
    }
}

public class InterfaceDouble : DispatchProxy
{
    Func<MethodInfo, object?[]?, object?> handler = null!;
    internal static T Create<T>(Func<MethodInfo, object?[]?, object?> handler) where T : class
    {
        T proxy = DispatchProxy.Create<T, InterfaceDouble>();
        ((InterfaceDouble)(object)proxy).handler = handler;
        return proxy;
    }
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => handler(targetMethod!, args);
}
