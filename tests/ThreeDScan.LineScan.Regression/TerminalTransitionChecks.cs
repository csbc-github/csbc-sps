using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ThreeDScan.Application.Common.Models;
using ThreeDScan.Application.Scan;
using ThreeDScan.Application.Scan.Models;
using ThreeDScan.Domain.Abstractions;
using ThreeDScan.Domain.Models;
using ThreeDScan.Domain.Repositories;

namespace ThreeDScan.LineScan.Regression;

internal static class TerminalTransitionChecks
{
    internal static async Task RunAsync()
    {
        foreach (bool expire in new[] { false, true })
        {
            await VerifyAsync(expire, racingStatus: null);
            foreach (string terminal in new[] { "Completed", "Failed", "Aborted" })
                await VerifyAsync(expire, terminal);
        }
        Console.WriteLine("PASS line abort and watchdog expiry preserve terminal tasks committed after their initial read");
    }

    private static async Task VerifyAsync(bool expire, string? racingStatus)
    {
        const string taskId = "terminal-transition-regression";
        var document = new TaskDocument
        {
            TaskId = taskId, Kind = "Line", ScannerId = "regression", Status = "Processing",
            Phase = "LineProcessing", TraceId = "terminal-transition-trace"
        };
        var cache = new DasTaskResultCache();
        cache.SetLatestLine(new TaskResultData { TaskId = taskId, Status = "Processing" });
        TaskResultData? winningPayload = null;
        string? winningResult = null;
        TaskApiError? winningError = null;
        int updates = 0, abortCalls = 0, releases = 0;

        Task<TaskDocument> Commit(Action<TaskDocument> mutation)
        {
            updates++;
            Program.Check(document.Status == "Processing", "abort/expiry first reads a nonterminal task");
            if (racingStatus is not null)
            {
                // Model another scope winning the repository gate between the caller's
                // initial read and its atomic mutation; no timing-dependent sleeps.
                document.Status = racingStatus;
                document.Phase = racingStatus;
                document.Result = racingStatus == "Completed" ? JsonSerializer.SerializeToElement(new { detectedCount = 1 }) : null;
                document.Error = racingStatus == "Failed" ? new TaskApiError { Code = "SCAN_LINE_002", Message = "measurement failed" } : null;
                document.CallbackUrl = "http://127.0.0.1/unused-test-callback";
                document.CallbackDelivered = true;
                document.CallbackAttempts = 1;
                winningResult = document.Result?.GetRawText();
                winningError = document.Error;
                winningPayload = new TaskResultData
                {
                    TaskId = taskId, Status = racingStatus, TraceId = document.TraceId,
                    Result = document.Result,
                    Error = document.Error is { } error ? new TaskErrorDto { Code = error.Code, Message = error.Message } : null
                };
                cache.SetLatestLine(winningPayload);
            }
            mutation(document);
            return Task.FromResult(document);
        }

        var repository = InterfaceDouble.Create<IScanTaskRepository>((method, args) => method.Name switch
        {
            nameof(IScanTaskRepository.GetAsync) => Task.FromResult(document),
            nameof(IScanTaskRepository.FindAsync) => Task.FromResult<TaskDocument?>(document),
            nameof(IScanTaskRepository.UpdateAsync) => Commit((Action<TaskDocument>)args![1]!),
            nameof(IScanTaskRepository.TaskDirectory) => Path.Combine(Path.GetTempPath(), taskId),
            _ => throw new NotSupportedException(method.Name)
        });
        var scanner = InterfaceDouble.Create<ISickLineScanner>((method, _) =>
        {
            if (method.Name != nameof(ISickLineScanner.AbortRecordingAsync)) throw new NotSupportedException(method.Name);
            abortCalls++;
            return Task.CompletedTask;
        });
        var devices = InterfaceDouble.Create<IDeviceCoordinator>((method, _) =>
        {
            if (method.Name != nameof(IDeviceCoordinator.ReleaseByTaskId)) throw new NotSupportedException(method.Name);
            releases++;
            return null;
        });
        var application = new ScanAppService(
            registry: null!, captureFactory: null!, pipeline: null!, taskService: null!, storage: null!, jobThickness: null!, scanCallLog: null!,
            taskResults: cache, workQueue: null!, callback: null!, scopeFactory: null!,
            surfaceScan: Options.Create(new SurfaceScanSettings()), lineScanners: null!, scanTaskStore: repository,
            lineScannerSettings: null!, devices: devices, lineDevice: null!, lineScanner: scanner, rollerCalibration: null!,
            lineScan: Options.Create(new LineScanSettings()), orchestrator: null!, logger: NullLogger<ScanAppService>.Instance);

        if (expire)
        {
            await application.ExpireLineScanAsync(taskId);
        }
        else
        {
            var aborted = await application.AbortLineScanAsync(new LineScanAbortRequest { TaskId = taskId });
            Program.Check(racingStatus is null ? aborted.Success : !aborted.Success && aborted.Error?.Code == "SCAN_LINE_001",
                "abort reports success only when its atomic terminal transition wins");
        }
        Program.Check(updates == 1, "terminal transition is checked within the repository mutation");

        if (racingStatus is not null)
        {
            Program.Check(document.Status == racingStatus && document.Phase == racingStatus
                && document.Result?.GetRawText() == winningResult && ReferenceEquals(document.Error, winningError),
                "late abort/expiry preserves the winning terminal state, result and error");
            Program.Check(document.CallbackDelivered && document.CallbackAttempts == 1,
                "late abort/expiry preserves completed callback bookkeeping");
            Program.Check(abortCalls == 0 && releases == 0,
                "late abort/expiry does not cancel recording or release an already-finished task again");
            Program.Check(cache.TryGet(taskId, out var cached) && ReferenceEquals(cached, winningPayload)
                && cache.TryGetLatestLine(out var latest) && ReferenceEquals(latest, winningPayload),
                "late abort/expiry never removes or replaces the winning terminal cache");
        }
        else
        {
            Program.Check(document.Status == "Aborted" && document.Phase == "Aborted" && abortCalls == 1 && releases == 1,
                "normal abort/expiry still transitions and cleans up exactly once");
            Program.Check(expire ? !cache.TryGet(taskId, out _) : cache.TryGet(taskId, out var aborted) && aborted?.Status == "Aborted",
                "normal expiry removes its cached task and normal abort publishes Aborted");
        }
    }
}
