using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ThreeDScan.Application.Common.Models;
using ThreeDScan.Application.Scan;
using ThreeDScan.Domain.Abstractions;
using ThreeDScan.Domain.Models;
using ThreeDScan.Domain.Repositories;
using ThreeDScan.Infrastructure.LineScan;
using ThreeDScan.Infrastructure.LineScan.Hank;

namespace ThreeDScan.LineScan.Regression;

internal static class LiveStateChecks
{
    internal static async Task RunAsync()
    {
        foreach (string phase in new[] { "LineAcquiring", "LineProcessing" })
        foreach (bool fresh in new[] { false, true })
            await VerifyLiveStateAsync(phase, "Processing", fresh, setupAvailable: true);
        foreach (string terminal in new[] { "Completed", "Failed", "Aborted" })
            await VerifyLiveStateAsync("LineProcessing", terminal, fresh: false, setupAvailable: true);
        await VerifyLiveStateAsync(phase: null, "Processing", fresh: false, setupAvailable: true);
        await VerifyLiveStateAsync(phase: null, "Processing", fresh: false, setupAvailable: false);
        await VerifyCacheIdempotenceAsync();
        Console.WriteLine("PASS live state stays busy through acquisition/processing and repeated preview starts preserve averaging");
    }

    static async Task VerifyLiveStateAsync(string? phase, string status, bool fresh, bool setupAvailable)
    {
        const string taskId = "live-state-regression";
        var document = phase is null ? null : new TaskDocument
        {
            TaskId = taskId, Kind = "Line", ScannerId = "regression", Status = status, Phase = phase
        };
        int recordingReads = 0, setupReads = 0;
        var repository = InterfaceDouble.Create<IScanTaskRepository>((method, _) => method.Name switch
        {
            // Include a terminal task to model it completing between listing and reading.
            nameof(IScanTaskRepository.NonTerminalTaskIdsAsync) => Task.FromResult<IReadOnlyList<string>>(
                document is null ? [] : [taskId]),
            nameof(IScanTaskRepository.FindAsync) => Task.FromResult<TaskDocument?>(document),
            _ => throw new NotSupportedException(method.Name)
        });
        var facade = InterfaceDouble.Create<ILineScanDeviceFacade>((method, args) =>
        {
            if (method.Name == nameof(ILineScanDeviceFacade.TryGetRecordingLive))
            {
                recordingReads++;
                Program.Check((string)args![0]! == taskId, "live recording snapshot uses the active task identity");
                args[1] = fresh ? new[] { 10d, 20d } : Array.Empty<double>();
                args[2] = fresh ? new[] { 100d, 200d } : Array.Empty<double>();
                return fresh;
            }
            if (method.Name == nameof(ILineScanDeviceFacade.TryGetSetupLive))
            {
                setupReads++;
                Program.Check((string)args![0]! == "regression", "idle live snapshot uses the configured scanner");
                args[1] = setupAvailable ? new[] { 30d, 40d } : Array.Empty<double>();
                args[2] = setupAvailable ? new[] { 300d, 400d } : Array.Empty<double>();
                return setupAvailable;
            }
            throw new NotSupportedException(method.Name);
        });
        var application = new ScanAppService(
            registry: null!, captureFactory: null!, pipeline: null!, taskService: null!, storage: null!, jobThickness: null!, scanCallLog: null!,
            taskResults: null!, workQueue: null!, callback: null!, scopeFactory: null!,
            surfaceScan: Options.Create(new SurfaceScanSettings()), lineScanners: null!, scanTaskStore: repository,
            lineScannerSettings: null!, devices: null!, lineDevice: facade, lineScanner: null!, rollerCalibration: null!,
            lineScan: Options.Create(new LineScanSettings { DefaultScannerId = "regression" }), orchestrator: null!,
            logger: NullLogger<ScanAppService>.Instance);
        var result = await application.GetLiveFrameAsync();
        bool busy = document is { Terminal: false };
        Program.Check(result.Recording == busy && result.TaskId == (busy ? taskId : ""),
            "active acquisition or processing remains busy even without a fresh averaged snapshot");
        Program.Check(recordingReads == (busy ? 1 : 0) && setupReads == (busy ? 0 : 1),
            "busy task never falls through to setup while a terminal task no longer blocks it");
        double[] expectedX = busy ? fresh ? [10d, 20d] : [] : setupAvailable ? [30d, 40d] : [];
        double[] expectedY = busy ? fresh ? [100d, 200d] : [] : setupAvailable ? [300d, 400d] : [];
        Program.Check(result.X.SequenceEqual(expectedX) && result.Y.SequenceEqual(expectedY),
            "live result keeps fresh native XY or returns aligned empty arrays while waiting");
    }

    static async Task VerifyCacheIdempotenceAsync()
    {
        // An unstarted scanner has no network connection; only its Connected flag
        // is supplied to exercise the real lease's cache and freshness guards.
        var scanner = new Scanner();
        typeof(Scanner).GetField("<Connected>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(scanner, true);
        await using var lease = new LineScanLease(scanner, ownsScanner: false);
        var receive = typeof(LineScanLease).GetMethod("Receive", BindingFlags.Instance | BindingFlags.NonPublic)!;
        void Feed(ushort frame, double x) => receive.Invoke(lease,
            [new Scan(frame, (uint)(1000 + frame * 10000), 100, 0, [new P(x, 500), new P(x + 10, 500)])]);

        lease.EnableLiveCache(3);
        Feed(0, 100);
        Feed(1, 102);
        Program.Check(!lease.TryGetLiveSnapshot(out _, out _), "preview averaging waits for the configured block size");
        lease.EnableLiveCache(3);
        Feed(2, 104);
        Program.Check(lease.TryGetLiveSnapshot(out var x, out var y) && x.Length == 2 && y.Length == 2,
            "repeated setup with unchanged average preserves the partially collected block");
        Program.Near(x[0], 102, "unchanged preview setup preserves all samples in the average");
        lease.EnableLiveCache(3);
        Program.Check(lease.TryGetLiveSnapshot(out var retainedX, out _) && retainedX.SequenceEqual(x),
            "repeated setup keeps the already completed preview snapshot");

        lease.EnableLiveCache(2);
        Program.Check(!lease.TryGetLiveSnapshot(out _, out _), "changing preview average clears the previous snapshot");
        Feed(3, 106);
        Program.Check(lease.TryGetLiveSnapshot(out var changedX, out _) && changedX.Length == 2,
            "changed preview average completes under the new block size");
        Program.Near(changedX[0], 105, "changed preview average resets the block and uses the latest/new sample pair");
    }
}
