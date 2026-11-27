using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ThreeDScan.Application.Common.Models;
using ThreeDScan.Application.Scan;
using ThreeDScan.Domain.Models;
using ThreeDScan.Domain.Repositories;

int passed = 0;
await Check("business failure survives persistence and GET/callback mapping", async () =>
{
    var repository = RepositoryWithProcessingTask();
    object details = new
    {
        warnings = new[] { "Held-out section edge does not agree." },
        validation = new { leftEdgeDistanceMm = 3.907, maximumSectionEdgeDistanceMm = 1.5 },
    };
    var executor = new ScanWorkExecutor(repository, null!, null!);
    await executor.MarkFailedAsync("SURFACE-test", new ScanTaskException(422,
        "STITCH_QUALITY_FAILED", "拼接檢查未通過。", details));

    var saved = repository.Document!;
    Require(saved.Status == "Failed" && saved.Phase == "Failed", "Failure must reach its terminal state.");
    Require(saved.Result is null, "A failed task cannot retain its previous result.");
    Require(saved.Error?.Code == "STITCH_QUALITY_FAILED" && saved.Error.Message == "拼接檢查未通過。",
        "Existing code and message must be preserved.");
    var savedDetails = JsonSerializer.SerializeToElement(saved.Error!.Details, JsonDefaults.Options);
    Require(savedDetails.GetProperty("validation").GetProperty("leftEdgeDistanceMm").GetDouble() == 3.907,
        "Numeric validation detail was lost in the repository round trip.");

    // Exercise the callback mapper and the actual public application method used
    // by GET /api/tasks/{taskId}/result; testing the mapper alone missed its
    // former duplicate polling implementation, which dropped Details.
    var payload = TaskResultDataMapper.FromDocument(saved);
    var serialized = JsonSerializer.SerializeToElement(payload, JsonDefaults.Options);
    var error = serialized.GetProperty("error");
    Require(error.GetProperty("details").GetProperty("warnings")[0].GetString() == "Held-out section edge does not agree.",
        "GET/callback serialization lost the original warning.");
    Require(error.GetProperty("details").GetProperty("validation").GetProperty("maximumSectionEdgeDistanceMm").GetDouble() == 1.5,
        "GET/callback serialization lost the validation limit.");
    Require(serialized.GetProperty("traceId").GetString() == "trace-test", "Trace ID changed during failure mapping.");

    var polling = await CreateScanAppService(repository).GetTaskResultAsync("SURFACE-test");
    Require(polling.Success && polling.Data?.Status == "Failed", "Polling did not return the saved failed task.");
    var pollingJson = JsonSerializer.SerializeToElement(polling.Data, JsonDefaults.Options);
    var pollingDetails = pollingJson.GetProperty("error").GetProperty("details");
    Require(pollingDetails.GetProperty("validation").GetProperty("leftEdgeDistanceMm").GetDouble() == 3.907
        && pollingDetails.GetProperty("validation").GetProperty("maximumSectionEdgeDistanceMm").GetDouble() == 1.5,
        "Public GET application path dropped the measured diagnostic value or original limit.");
    Require(pollingDetails.GetProperty("warnings")[0].GetString() == "Held-out section edge does not agree."
        && pollingJson.GetProperty("traceId").GetString() == "trace-test",
        "Public GET application path changed the warning or trace ID.");
});

await Check("business failure without details retains legacy error shape", async () =>
{
    var repository = RepositoryWithProcessingTask();
    await new ScanWorkExecutor(repository, null!, null!).MarkFailedAsync("SURFACE-test",
        new ScanTaskException(409, "NO_SEGMENTS", "尚無掃描段落。"));
    var payload = JsonSerializer.SerializeToElement(TaskResultDataMapper.FromDocument(repository.Document!), JsonDefaults.Options);
    var error = payload.GetProperty("error");
    Require(error.EnumerateObject().Count() == 2 && !error.TryGetProperty("details", out _),
        "Missing diagnostics must not add a null property to the existing response contract.");
});

await Check("unexpected exception remains internal failure without fabricated details", async () =>
{
    var repository = RepositoryWithProcessingTask();
    await new ScanWorkExecutor(repository, null!, null!).MarkFailedAsync("SURFACE-test", new InvalidOperationException("Storage unavailable."));
    Require(repository.Document!.Error?.Code == "SYS_INTERNAL", "Unexpected exceptions need the existing internal code.");
    Require(repository.Document.Error?.Message == "Storage unavailable." && repository.Document.Error.Details is null,
        "Unexpected exception mapping changed.");
});

await Check("completed task remains immutable", async () =>
{
    var repository = RepositoryWithProcessingTask();
    repository.Document!.Status = "Completed";
    repository.Document.Phase = "Completed";
    await new ScanWorkExecutor(repository, null!, null!).MarkFailedAsync("SURFACE-test", new ScanTaskException(422, "STITCH_QUALITY_FAILED", "Failed", new { warning = "late" }));
    Require(repository.Updates == 0 && repository.Document.Status == "Completed" && repository.Document.Error is null,
        "A late worker failure overwrote a completed task.");
});

await Check("terminal race remains immutable inside update", async () =>
{
    var repository = RepositoryWithProcessingTask();
    repository.BeforeUpdate = document => { document.Status = "Aborted"; document.Phase = "Aborted"; };
    await new ScanWorkExecutor(repository, null!, null!).MarkFailedAsync("SURFACE-test", new ScanTaskException(422, "STITCH_QUALITY_FAILED", "Failed", new { warning = "late" }));
    Require(repository.Updates == 1 && repository.Document!.Status == "Aborted" && repository.Document.Error is null,
        "A concurrent abort was overwritten inside the update lock.");
});

await Check("missing task does not create a failure document", async () =>
{
    var repository = new RoundTripRepository();
    await new ScanWorkExecutor(repository, null!, null!).MarkFailedAsync("missing", new InvalidOperationException("Failure"));
    Require(repository.Updates == 0 && repository.Document is null, "A missing task caused a repository update.");
});

await Check("nonfailed result mapping does not leak stale diagnostics", () =>
{
    var document = RepositoryWithProcessingTask().Document!;
    document.Error = new TaskApiError { Code = "OLD", Message = "Old failure", Details = new { warning = "old" } };
    foreach (string status in new[] { "Processing", "Completed", "Aborted" })
    {
        document.Status = status;
        Require(TaskResultDataMapper.FromDocument(document).Error is null, $"Stale diagnostics leaked into {status}.");
    }
    return Task.CompletedTask;
});

await Check("strictly passing stitch remains completable in either policy", () =>
{
    var result = new StitchProcessingResult { ValidationPassed = true };
    Require(StitchCompletionPolicy.CanCompletePointCloud(result, requireVerifiedBoundaries: true)
        && StitchCompletionPolicy.CanCompletePointCloud(result, requireVerifiedBoundaries: false),
        "A fully validated legacy result must remain completable without newly added flags.");
    return Task.CompletedTask;
});

await Check("boundary-only completion retains actual failed measurement and strict flags", () =>
{
    var result = PolicyResult(true, false, new StitchQualityFailure
    {
        SeamNumber = 1, Check = "rightEndInteriorCoverage.leftEdgeDistanceMm", ActualValue = 2.063516400210464,
        Limit = 1.5, Comparison = "<=", Unit = "mm", Message = "後段端部翼緣無法確認。",
    });
    string before = JsonSerializer.Serialize(result, JsonDefaults.Options);
    Require(StitchCompletionPolicy.CanCompletePointCloud(result, requireVerifiedBoundaries: false),
        "The user-selected point-cloud policy must allow a recorded boundary-only failure after alignment passes.");
    Require(!StitchCompletionPolicy.CanCompletePointCloud(result, requireVerifiedBoundaries: true),
        "Requiring verified boundaries must retain the strict failure.");
    Require(before == JsonSerializer.Serialize(result, JsonDefaults.Options) && !result.ValidationPassed,
        "Completion policy must not rewrite failed physical validation as a successful validation.");
    var persisted = JsonSerializer.Deserialize<StitchProcessingResult>(before, JsonDefaults.Options)!;
    Require(persisted.AlignmentValidationPassed == true && persisted.BoundaryValidationPassed == false
        && persisted.ValidationFailures.Single().ActualValue == 2.063516400210464
        && persisted.ValidationFailures.Single().Limit == 1.5
        && StitchCompletionPolicy.CanCompletePointCloud(persisted, requireVerifiedBoundaries: false),
        "Serialized policy evaluation must retain the actual measured failure and its original limit.");
    return Task.CompletedTask;
});

await Check("only the nine explicit boundary checks may be downgraded", () =>
{
    string[] boundaryChecks =
    [
        "fullSection.leftEdgeDistanceMm", "fullSection.rightEdgeDistanceMm", "fullSection.commonCoverageRatio",
        "leftEndInteriorCoverage.leftEdgeDistanceMm", "leftEndInteriorCoverage.rightEdgeDistanceMm", "leftEndInteriorCoverage.commonCoverageRatio",
        "rightEndInteriorCoverage.leftEdgeDistanceMm", "rightEndInteriorCoverage.rightEdgeDistanceMm", "rightEndInteriorCoverage.commonCoverageRatio",
    ];
    var result = PolicyResult(true, false, boundaryChecks.Select(check => new StitchQualityFailure { Check = check }).ToArray());
    Require(StitchCompletionPolicy.CanCompletePointCloud(result, requireVerifiedBoundaries: false),
        "The documented edge-distance and coverage failures must be recognized for every section.");
    Require(!StitchCompletionPolicy.CanCompletePointCloud(result, requireVerifiedBoundaries: true),
        "The strict policy cannot ignore any boundary failure.");
    return Task.CompletedTask;
});

await Check("alignment and boundary flags must be explicit before warning-only completion", () =>
{
    foreach (bool? alignment in new bool?[] { null, false, true })
        foreach (bool? boundary in new bool?[] { null, false, true })
        {
            var result = PolicyResult(alignment, boundary, new StitchQualityFailure { Check = "fullSection.leftEdgeDistanceMm" });
            bool expected = alignment == true && boundary == false;
            Require(StitchCompletionPolicy.CanCompletePointCloud(result, requireVerifiedBoundaries: false) == expected,
                $"Unexpected permissive policy decision for alignment={alignment}, boundary={boundary}.");
            Require(!StitchCompletionPolicy.CanCompletePointCloud(result, requireVerifiedBoundaries: true),
                "A strict failed result must never complete merely because its new flags are present.");
        }
    return Task.CompletedTask;
});

await Check("RMS support and unknown failures always block point-cloud completion", () =>
{
    foreach (string check in new[]
    {
        "crossSectionHeightRmsMm", "crossSectionHeightP95AbsoluteMm", "leftEndWingSupportFraction", "rightEndWingSupportFraction",
        "fullSection.newMetric", "FullSection.leftEdgeDistanceMm", "fullSection.leftEdgeDistanceMm.extra", "", "unknown",
    })
    {
        var result = PolicyResult(true, false,
            new StitchQualityFailure { Check = "fullSection.leftEdgeDistanceMm" }, new StitchQualityFailure { Check = check });
        Require(!StitchCompletionPolicy.CanCompletePointCloud(result, requireVerifiedBoundaries: false),
            $"A boundary warning must not conceal the non-whitelisted failure {check}.");
    }
    return Task.CompletedTask;
});

await Check("empty malformed and legacy failure reports fail closed", () =>
{
    var empty = PolicyResult(true, false);
    var malformed = PolicyResult(true, false, new StitchQualityFailure[] { null! });
    var nullFailures = new StitchProcessingResult { AlignmentValidationPassed = true, BoundaryValidationPassed = false, ValidationFailures = null! };
    foreach (var result in new[] { empty, malformed, nullFailures })
        Require(!StitchCompletionPolicy.CanCompletePointCloud(result, requireVerifiedBoundaries: false),
            "Missing or malformed failure evidence cannot be treated as a known boundary-only failure.");
    var legacy = JsonSerializer.Deserialize<StitchProcessingResult>("""
        {"validationPassed":false,"validationFailures":[{"check":"fullSection.leftEdgeDistanceMm"}]}
        """, JsonDefaults.Options)!;
    Require(legacy.AlignmentValidationPassed is null && legacy.BoundaryValidationPassed is null
        && !StitchCompletionPolicy.CanCompletePointCloud(legacy, requireVerifiedBoundaries: false),
        "An older report without explicit alignment and boundary results cannot authorize warning-only completion.");
    return Task.CompletedTask;
});

Console.WriteLine($"PASS: {passed} task-failure checks.");

async Task Check(string name, Func<Task> check)
{
    await check();
    passed++;
    Console.WriteLine($"PASS {name}");
}

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static RoundTripRepository RepositoryWithProcessingTask() => new()
{
    Document = new TaskDocument
    {
        TaskId = "SURFACE-test", Kind = "Surface", Status = "Processing", Phase = "Stitching", TraceId = "trace-test",
        Result = JsonDefaults.Element(new { previous = true }),
    },
};

static StitchProcessingResult PolicyResult(bool? alignment, bool? boundary, params StitchQualityFailure[] failures) => new()
{
    ValidationPassed = false, AlignmentValidationPassed = alignment, BoundaryValidationPassed = boundary, ValidationFailures = failures,
};

static ScanAppService CreateScanAppService(RoundTripRepository repository)
{
    var orchestrator = DispatchProxy.Create<IScanOrchestrator, FindOnlyOrchestratorProxy>();
    ((FindOnlyOrchestratorProxy)(object)orchestrator).Repository = repository;
    return new ScanAppService(
        registry: null!, captureFactory: null!, pipeline: null!, taskService: null!, storage: null!,
        jobThickness: null!, scanCallLog: null!, taskResults: null!, workQueue: null!, callback: null!, scopeFactory: null!,
        surfaceScan: Options.Create(new SurfaceScanSettings()), lineScanners: null!, scanTaskStore: repository,
        lineScannerSettings: null!, devices: null!, lineDevice: null!, lineScanner: null!, rollerCalibration: null!,
        lineScan: Options.Create(new LineScanSettings()), orchestrator: orchestrator, logger: NullLogger<ScanAppService>.Instance);
}

// Any accidental capture, processing or external-service call fails immediately.
public class FindOnlyOrchestratorProxy : DispatchProxy
{
    public IScanTaskRepository Repository { get; set; } = null!;

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod?.Name == nameof(IScanOrchestrator.FindAsync))
            return Repository.FindAsync((string)args![0]!, (CancellationToken)args[1]!);
        throw new NotSupportedException("Only persisted task lookup is permitted in this regression.");
    }
}

// Exercise real serialization on every repository read/write so details become JsonElement,
// matching persisted task documents rather than retaining a reference to the thrown exception.
sealed class RoundTripRepository : IScanTaskRepository
{
    public TaskDocument? Document { get; set; }
    public int Updates { get; private set; }
    public Action<TaskDocument>? BeforeUpdate { get; set; }
    public string CallbackMode => "PollingOnly";
    public string Root => "unused";
    public string TaskDirectory(string id) => throw new NotSupportedException();
    public Task<TaskDocument?> FindAsync(string id, CancellationToken ct = default) => Task.FromResult(Document is null ? null : Clone(Document));
    public Task<TaskDocument> GetAsync(string id, string? kind = null, CancellationToken ct = default) => Task.FromResult(Clone(Document!));
    public Task CreateAsync(TaskDocument doc, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<TaskDocument> UpdateAsync(string id, Action<TaskDocument> mutation, CancellationToken ct = default)
    {
        Updates++;
        var document = Clone(Document!);
        BeforeUpdate?.Invoke(document);
        mutation(document);
        Document = Clone(document);
        return Task.FromResult(Clone(Document));
    }
    public Task<TaskDocument> CommitAsync(string id, Func<TaskDocument, Task> mutation, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<string>> TaskIdsAsync(CancellationToken ct = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<string>> NonTerminalTaskIdsAsync(CancellationToken ct = default) => throw new NotSupportedException();
    public Task<int> RecoverAfterServiceRestartAsync(CancellationToken ct = default) => throw new NotSupportedException();
    public TaskEnvelope Envelope(TaskDocument document) => throw new NotSupportedException();
    private static TaskDocument Clone(TaskDocument document) => JsonSerializer.Deserialize<TaskDocument>(JsonSerializer.Serialize(document, JsonDefaults.Options), JsonDefaults.Options)!;
}
