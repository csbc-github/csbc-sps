using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ThreeDScan.Controls;
using ThreeDScan.Domain.Models;
using ThreeDScan.Models;
using ThreeDScan.ViewModels;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources["BorderBrush"] = Brushes.Gray;
        app.Resources["BackgroundBrush"] = Brushes.White;
        app.Resources["TextSecondaryBrush"] = Brushes.Gray;
        app.Resources["FontSizeLarge"] = 16d;
        app.Resources["FontSizeSmall"] = 12d;
        // Suppress the production VM constructor's timers, scanner probes and startup HTTP.
        DesignerProperties.IsInDesignModeProperty.OverrideMetadata(typeof(DependencyObject), new FrameworkPropertyMetadata(true));
        var exitCode = 1;
        app.Dispatcher.BeginInvoke(async () =>
        {
            try
            {
                await RunAsync(app.Dispatcher);
                Console.WriteLine("PASS: WPF selection stays on the UI dispatcher; live preview recovers after API recording without overriding user controls.");
                exitCode = 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
            }
            finally
            {
                app.Shutdown();
            }
        });
        app.Run();
        return exitCode;
    }

    private static async Task RunAsync(Dispatcher dispatcher)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing", Args = [] });
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var server = builder.Build();
        var profileRequests = 0;
        var preview = new PreviewFixture();
        server.MapPost("/api/scan/profile", async () =>
        {
            Interlocked.Increment(ref profileRequests);
            // Force HTTP continuations to leave the original UI call stack.
            await Task.Delay(30);
            return Results.Json(new { points = new[] { new { x = -50d, y = -500d }, new { x = 50d, y = -500d } } });
        });
        server.MapPost("/api/scanners/{scannerId}/roi", async (HttpRequest request) =>
        {
            using var body = await JsonDocument.ParseAsync(request.Body);
            var bounds = body.RootElement.GetProperty("bounds");
            await Task.Delay(30);
            return Results.Json(new { roiXY = bounds.ValueKind == JsonValueKind.Null ? null : bounds.EnumerateArray().Select(x => x.GetDouble()).ToArray() });
        });
        server.MapPost("/api/scanners/{scannerId}/calibration/two-point", async (HttpRequest request) =>
        {
            using var body = await JsonDocument.ParseAsync(request.Body);
            var calibration = body.RootElement.Clone();
            await Task.Delay(30);
            return Results.Json(new { calibration });
        });
        server.MapDelete("/api/scanners/{scannerId}/calibration/two-point", async () =>
        {
            await Task.Delay(30);
            return Results.Json(new { calibration = (object?)null });
        });
        server.MapPost("/api/scan/line/setup/start", preview.StartAsync);
        server.MapPost("/api/scan/line/setup/stop", preview.StopAsync);
        server.MapGet("/api/scan/line/live", preview.LiveAsync);
        server.MapGet("/api/scan/line/latest", async () =>
        {
            await Task.Delay(30);
            return Results.Json(new
            {
                taskId = "TEST-FAILED",
                status = "Failed",
                error = new { code = "SCAN_LINE_002", message = "角鋼特徵只出現於單一截面，無法對應頭尾：X=290.8 mm。" }
            });
        });
        await server.StartAsync();
        var address = server.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();

        // Only replace this isolated test executable's output configuration, restoring it afterward.
        var paths = new[] { "appsettings.json", "appsettings.Development.json" }.Select(x => Path.Combine(AppContext.BaseDirectory, x)).ToArray();
        var backups = paths.ToDictionary(x => x, x => File.Exists(x) ? File.ReadAllBytes(x) : null);
        try
        {
            var config = JsonSerializer.Serialize(new { CalibrationMonitor = new { ApiBaseUrl = address }, LineScan = new { DefaultScannerId = "TEST-ONLY" } });
            foreach (var path in paths) File.WriteAllText(path, config);
            var vm = new FeedingViewModel();
            var view = new LidarScanView { ProfileSnapPoints = vm.ProfileSnapPoints };
            vm.PropertyChanged += (_, _) => dispatcher.VerifyAccess();
            vm.ProfileSnapPoints.CollectionChanged += (_, _) => view.Dispatcher.VerifyAccess();
            SetField(vm, "_userStoppedLive", true);

            await InvokeAsync(vm, "BeginSelectRegionAsync");
            Require(vm.IsSelectingRegion && vm.IsProfileFrozen && vm.ProfileSnapPoints.Count == 2, "ROI selection must freeze and retain the profile.");
            await InvokeAsync(vm, "BeginSelectRegionAsync");
            Require(!vm.IsSelectingRegion && !vm.IsProfileFrozen && vm.ProfileSnapPoints.Count == 0, "Cancel must clear the frozen profile.");

            await InvokeAsync(vm, "BeginSelectRegionAsync");
            var region = new LineScanRegion { XMin = -60, XMax = 60, YMin = -510, YMax = -490 };
            vm.SensorRegion = region;
            await InvokeAsync(vm, "SaveRegionAndResumeAsync", region);
            Require(!vm.IsProfileFrozen && vm.ProfileSnapPoints.Count == 0, "Saving ROI must resume safely after asynchronous HTTP.");
            await InvokeAsync(vm, "ClearRegionAsync");
            Require(vm.SensorRegion is null, "Clearing ROI must clear its visual state.");

            await InvokeAsync(vm, "SetDesktopCalibrationAsync");
            Require(vm.IsPickingDesktopPoints && vm.IsProfileFrozen, "Two-point selection must freeze the profile.");
            vm.DesktopFirstX = -50;
            vm.DesktopFirstY = -500;
            vm.DesktopSecondX = 50;
            vm.DesktopSecondY = -500;
            await InvokeAsync(vm, "SubmitTwoPointAndResumeAsync");
            Require(!vm.IsProfileFrozen && vm.ProfileSnapPoints.Count == 0, "Saving two-point calibration must resume on the UI dispatcher.");
            await InvokeAsync(vm, "SetDesktopCalibrationAsync");
            await InvokeAsync(vm, "ClearDesktopCalibrationAsync");
            Require(vm.DesktopFirstX is null && vm.DesktopSecondX is null && !vm.IsProfileFrozen, "Clearing calibration must clear markers and resume.");

            await InvokeAsync(vm, "StartAsync");
            await InvokeAsync(vm, "RefreshLiveAsync");
            Require(vm.IsScanning, "Starting preview must update IsScanning safely.");
            await InvokeAsync(vm, "StopAsync");
            Require(!vm.IsScanning, "Stopping preview must update IsScanning safely.");
            await InvokeAsync(vm, "RefreshLineResultAsync");
            Require(vm.LineResultHeadline == "失敗" && vm.LineOverallStatus == SystemStatus.Error
                && vm.LineResultStatusText.Contains("SCAN_LINE_002") && vm.LineResultStatusText.Contains("X=290.8"),
                "Result polling must display the failed task and actionable measurement error on the UI dispatcher.");
            Require(profileRequests == 4, "The selection test must execute four real asynchronous profile requests.");
            await VerifyPreviewRecoveryAsync(vm, preview);
        }
        finally
        {
            foreach (var path in paths)
            {
                if (backups[path] is { } original) File.WriteAllBytes(path, original);
                else File.Delete(path);
            }
            await server.StopAsync();
        }
    }

    private static async Task VerifyPreviewRecoveryAsync(FeedingViewModel vm, PreviewFixture preview)
    {
        SetField(vm, "_userStoppedLive", false);
        SetField(vm, "_nextPreviewStartAtUtc", DateTimeOffset.MinValue);
        int starts = preview.StartRequests;

        preview.Mode = PreviewFixture.Recording;
        await InvokeAsync(vm, "RefreshLiveAsync");
        Require(vm.ProfileSnapPoints.Count == 2 && preview.StartRequests == starts,
            "An API recording must display its live profile without requesting a competing setup connection.");
        preview.Mode = PreviewFixture.EmptyRecording;
        await InvokeAsync(vm, "RefreshLiveAsync");
        Require(preview.StartRequests == starts, "A recording with no averaged frame yet must not restart setup.");

        // Completing the API recording closes its socket. The first idle response has
        // no profile; a separate setup/start is required to establish preview again.
        preview.Mode = PreviewFixture.Idle;
        await InvokeAsync(vm, "RefreshLiveAsync");
        Require(preview.StartRequests == starts + 1, "The first empty idle response after recording must request setup recovery.");
        await InvokeAsync(vm, "RefreshLiveAsync");
        Require(vm.ProfileSnapPoints.Count == 2 && vm.IsScanning && preview.StartRequests == starts + 1,
            "The next live poll must restore native preview points without starting setup repeatedly.");

        // The completion task may still own the scanner while it reconstructs and
        // measures. A rejected restart must remain retryable after the throttle.
        preview.Mode = PreviewFixture.Idle;
        preview.Busy = true;
        SetField(vm, "_nextPreviewStartAtUtc", DateTimeOffset.MinValue);
        await InvokeAsync(vm, "RefreshLiveAsync");
        int rejectedStart = preview.StartRequests;
        Require(rejectedStart == starts + 2 && !vm.IsScanning,
            "A busy scanner must not be represented as an active setup preview.");
        await InvokeAsync(vm, "RefreshLiveAsync");
        Require(preview.StartRequests == rejectedStart, "Empty live polling must respect the setup retry throttle.");
        preview.Busy = false;
        SetField(vm, "_nextPreviewStartAtUtc", DateTimeOffset.MinValue);
        await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => InvokeAsync(vm, "RefreshLiveAsync")));
        Require(preview.StartRequests == rejectedStart + 1 && preview.MaxConcurrentStarts == 1,
            "Concurrent timer polls must produce one setup restart after the retry becomes due.");
        await InvokeAsync(vm, "RefreshLiveAsync");
        Require(vm.IsScanning && vm.ProfileSnapPoints.Count == 2, "A temporarily busy scanner must recover on a later poll.");

        // Explicit user actions retain control over the frozen graph and connection.
        preview.Mode = PreviewFixture.Idle;
        SetField(vm, "_nextPreviewStartAtUtc", DateTimeOffset.MinValue);
        starts = preview.StartRequests;
        vm.IsProfileFrozen = true;
        await InvokeAsync(vm, "RefreshLiveAsync");
        vm.IsProfileFrozen = false;
        vm.IsSelectingRegion = true;
        await InvokeAsync(vm, "RefreshLiveAsync");
        vm.IsSelectingRegion = false;
        vm.IsPickingDesktopPoints = true;
        await InvokeAsync(vm, "RefreshLiveAsync");
        vm.IsPickingDesktopPoints = false;
        Require(preview.StartRequests == starts, "Frozen, region-selection and two-point-selection modes must suppress automatic setup recovery.");
        await InvokeAsync(vm, "StopAsync");
        await InvokeAsync(vm, "RefreshLiveAsync");
        Require(preview.StartRequests == starts && !vm.IsScanning,
            "A manual preview stop must remain stopped when the live endpoint returns empty data.");
        await VerifyPendingStartStopAsync(vm, preview, cleanup: false);
        await VerifyPendingStartStopAsync(vm, preview, cleanup: true);
        Console.WriteLine("PASS preview recording-to-idle recovery, busy retry, throttle, concurrency and user-stop/selection guards");
    }

    private static async Task VerifyPendingStartStopAsync(FeedingViewModel vm, PreviewFixture preview, bool cleanup)
    {
        preview.Mode = PreviewFixture.Idle;
        int stops = preview.StopRequests;
        var heldStart = preview.HoldNextStart();
        var starting = InvokeAsync(vm, "StartAsync");
        try
        {
            await heldStart.Entered.WaitAsync(TimeSpan.FromSeconds(5));
            if (cleanup) await vm.CleanupAsync();
            else await InvokeAsync(vm, "StopAsync");
            Require(GetField<bool>(vm, "_userStoppedLive") && !vm.IsScanning && preview.Mode == PreviewFixture.Idle,
                "Stopping or closing while setup/start is pending must immediately preserve the user's stopped intent.");
        }
        finally
        {
            heldStart.Release();
            await starting.WaitAsync(TimeSpan.FromSeconds(5));
        }
        Require(GetField<bool>(vm, "_userStoppedLive") && !vm.IsScanning && preview.Mode == PreviewFixture.Idle
            && preview.StopRequests == stops + 2,
            "A late successful setup/start must be closed again without re-enabling preview after stop or cleanup.");
        int starts = preview.StartRequests;
        SetField(vm, "_nextPreviewStartAtUtc", DateTimeOffset.MinValue);
        await InvokeAsync(vm, "RefreshLiveAsync");
        Require(preview.StartRequests == starts && !vm.IsScanning,
            "Live polling after a stopped in-flight start must not reopen preview.");
    }

    private sealed class PreviewFixture
    {
        internal const int Idle = 0, Recording = 1, Preview = 2, EmptyRecording = 3;
        private readonly object _gate = new();
        private int _mode, _busy, _startRequests, _stopRequests, _activeStarts, _maxConcurrentStarts;
        private TaskCompletionSource? _startEntered, _releaseStart;
        internal int Mode { get => Volatile.Read(ref _mode); set => Volatile.Write(ref _mode, value); }
        internal bool Busy { get => Volatile.Read(ref _busy) != 0; set => Volatile.Write(ref _busy, value ? 1 : 0); }
        internal int StartRequests { get { lock (_gate) return _startRequests; } }
        internal int StopRequests => Volatile.Read(ref _stopRequests);
        internal int MaxConcurrentStarts { get { lock (_gate) return _maxConcurrentStarts; } }

        internal (Task Entered, Action Release) HoldNextStart()
        {
            lock (_gate)
            {
                _startEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
                var release = _releaseStart = new(TaskCreationOptions.RunContinuationsAsynchronously);
                return (_startEntered.Task, () => release.TrySetResult());
            }
        }

        internal async Task<IResult> StartAsync()
        {
            TaskCompletionSource? entered, release;
            lock (_gate)
            {
                _startRequests++;
                _maxConcurrentStarts = Math.Max(_maxConcurrentStarts, ++_activeStarts);
                entered = _startEntered;
                release = _releaseStart;
                _startEntered = _releaseStart = null;
            }
            try
            {
                entered?.TrySetResult();
                if (release is not null) await release.Task;
                await Task.Delay(30);
                if (Busy) return Results.BadRequest(new { code = "SCANNER_BUSY", message = "Test recording is still processing." });
                Mode = Preview;
                return Results.Ok();
            }
            finally { lock (_gate) _activeStarts--; }
        }

        internal async Task<IResult> StopAsync()
        {
            Interlocked.Increment(ref _stopRequests);
            await Task.Delay(30);
            Mode = Idle;
            return Results.Ok();
        }

        internal async Task<IResult> LiveAsync()
        {
            await Task.Delay(30);
            int mode = Mode;
            bool hasPoints = mode is Recording or Preview;
            return Results.Json(new
            {
                recording = mode is Recording or EmptyRecording,
                taskId = mode is Recording or EmptyRecording ? "TEST-RECORDING" : "",
                x = hasPoints ? new[] { -50d, 50d } : Array.Empty<double>(),
                y = hasPoints ? new[] { -500d, -500d } : Array.Empty<double>()
            });
        }
    }

    private static async Task InvokeAsync(FeedingViewModel vm, string name, params object?[] arguments)
    {
        var method = typeof(FeedingViewModel).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new MissingMethodException(name);
        await (Task)method.Invoke(vm, arguments)!;
    }

    private static void SetField(FeedingViewModel vm, string name, object value) =>
        typeof(FeedingViewModel).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(vm, value);

    private static T GetField<T>(FeedingViewModel vm, string name) =>
        (T)typeof(FeedingViewModel).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(vm)!;

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
