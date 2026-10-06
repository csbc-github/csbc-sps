using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ThreeDScan.Application.Common;
using ThreeDScan.Application.Common.Models;
using ThreeDScan.Application.Scan.Models;
using ThreeDScan.Domain.Abstractions;
using ThreeDScan.Domain.Models;
using ThreeDScan.Domain.Repositories;

namespace ThreeDScan.Application.Scan;

/// <summary>掃描應用服務：面掃描走 C# Pipeline；線掃描可選 Hank Python 或 C# 改寫；孔洞尚未實作。</summary>
public sealed class ScanAppService : IScanAppService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly IScannerRegistry _registry;
    private readonly IScannerCaptureFactory _captureFactory;
    private readonly ISurfaceScanPipeline _pipeline;
    private readonly ISurfaceScanTaskService _taskService;
    private readonly IStorageSettings _storage;
    private readonly IJobThicknessRepository _jobThickness;
    private readonly IScanCallLogRepository _scanCallLog;
    private readonly IDasTaskResultCache _taskResults;
    private readonly IAcceptedWorkQueue _workQueue;
    private readonly ICallbackNotifier _callback;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SurfaceScanSettings _surfaceScan;
    private readonly ILineScannerRegistry _lineScanners;
    private readonly ILineScanTaskService _lineTasks;
    private readonly ISickLineScanner _lineScanner;
    private readonly ICSharpLineMeasure _csharpMeasure;
    private readonly ILineScanRegionStore _lineRegion;
    private readonly IRollerCalibrationRepository _rollerCalibration;
    private readonly LineScanSettings _lineScan;
    private readonly ILogger<ScanAppService> _logger;

    /// <summary>
    /// 建立面掃描應用服務。
    /// </summary>
    /// <param name="registry">掃描器設定（含 MachineArea）。</param>
    /// <param name="captureFactory">依 scannerId 取得擷取實作。</param>
    /// <param name="pipeline">高度圖 → Pipeline 匯出。</param>
    /// <param name="taskService">面掃描任務（seq／附件路徑）暫存。</param>
    /// <param name="storage">匯出根目錄，用來把絕對路徑轉成 <c>/scan-files/</c> URL。</param>
    /// <param name="jobThickness">工單厚度：寫入與依 jobId 讀出。</param>
    /// <param name="scanCallLog">掃描 API 呼叫紀錄。</param>
    /// <param name="taskResults">指南任務結果快取。</param>
    /// <param name="workQueue">202 接受後的背景工作佇列。</param>
    /// <param name="callback">完成後 POST 台船 callbackUrl。</param>
    /// <param name="scopeFactory">背景工作另開 scope，避免捕捉 Scoped 服務。</param>
    /// <param name="surfaceScan">正式面掃描是否改讀既有 ply。</param>
    /// <param name="logger">寫入呼叫紀錄失敗時記警告，掃描 API 仍回已組好的回應。</param>
    public ScanAppService(
        IScannerRegistry registry,
        IScannerCaptureFactory captureFactory,
        ISurfaceScanPipeline pipeline,
        ISurfaceScanTaskService taskService,
        IStorageSettings storage,
        IJobThicknessRepository jobThickness,
        IScanCallLogRepository scanCallLog,
        IDasTaskResultCache taskResults,
        IAcceptedWorkQueue workQueue,
        ICallbackNotifier callback,
        IServiceScopeFactory scopeFactory,
        IOptions<SurfaceScanSettings> surfaceScan,
        ILineScannerRegistry lineScanners,
        ILineScanTaskService lineTasks,
        ISickLineScanner lineScanner,
        ICSharpLineMeasure csharpMeasure,
        ILineScanRegionStore lineRegion,
        IRollerCalibrationRepository rollerCalibration,
        IOptions<LineScanSettings> lineScan,
        ILogger<ScanAppService> logger)
    {
        _registry = registry;
        _captureFactory = captureFactory;
        _pipeline = pipeline;
        _taskService = taskService;
        _storage = storage;
        _jobThickness = jobThickness;
        _scanCallLog = scanCallLog;
        _taskResults = taskResults;
        _workQueue = workQueue;
        _callback = callback;
        _scopeFactory = scopeFactory;
        _surfaceScan = surfaceScan.Value ?? new SurfaceScanSettings();
        _lineScanners = lineScanners;
        _lineTasks = lineTasks;
        _lineScanner = lineScanner;
        _csharpMeasure = csharpMeasure;
        _lineRegion = lineRegion;
        _rollerCalibration = rollerCalibration;
        _lineScan = lineScan.Value ?? new LineScanSettings();
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<DasOperationResult<LineScanStartData>> StartLineScanAsync(
        LineScanRequest request,
        CancellationToken ct = default)
    {
        var traceId = Guid.NewGuid().ToString("N");
        if (!_lineScanners.TryGet(request.ScannerId, out var endpoint))
        {
            return DasOperationResult<LineScanStartData>.Fail("SCAN_LINE_002", "未知的線掃描 scannerId。");
        }

        var baseline = await _rollerCalibration.GetAsync(request.ScannerId, ct).ConfigureAwait(false);
        if (baseline is null)
        {
            return DasOperationResult<LineScanStartData>.Fail("SCAN_LINE_003", "尚未完成滾輪校正，無法提供絕對高度。");
        }

        if (_lineTasks.TryGetActiveByScanner(request.ScannerId, out _))
        {
            return DasOperationResult<LineScanStartData>.Fail("SCAN_LINE_004", "此光達已有進行中的線掃描。");
        }

        if (endpoint.Port <= 0 || endpoint.Port > 65535)
        {
            return DasOperationResult<LineScanStartData>.Fail("SCAN_LINE_002", "線掃描埠無效。");
        }

        var task = _lineTasks.Create(request.ScannerId.Trim(), request.RollerSpeed, traceId);
        try
        {
            await _lineScanner.StartRecordingAsync(task.SessionDirectory, endpoint.Ip, endpoint.Port, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is InvalidOperationException or TimeoutException or IOException)
        {
            await _lineScanner.AbortRecordingAsync(task.SessionDirectory, ct).ConfigureAwait(false);
            _lineTasks.Remove(task.TaskId);
            return DasOperationResult<LineScanStartData>.Fail("SCAN_LINE_002", ex.Message);
        }

        RememberProcessing(task.TaskId, traceId);
        return DasOperationResult<LineScanStartData>.Ok(new LineScanStartData
        {
            TaskId = task.TaskId,
            StartedAt = task.StartedAt.ToString("o"),
            TraceId = traceId,
        });
    }

    /// <inheritdoc />
    public Task<DasOperationResult<AsyncAcceptedData>> CompleteLineScanAsync(
        LineScanCompleteRequest request,
        CancellationToken ct = default)
    {
        _ = ct;
        var taskId = request.TaskId.Trim();
        if (!_lineTasks.TryGet(taskId, out var task) || task is null || task.Aborted || task.IsCompleted)
        {
            return Task.FromResult(
                DasOperationResult<AsyncAcceptedData>.Fail("SCAN_LINE_001", "taskId 不存在或已逾時。"));
        }

        if (!task.Completing)
        {
            task.Completing = true;
            _lineTasks.Save(task);
            var callbackUrl = request.CallbackUrl;
            var scopeFactory = _scopeFactory;
            _workQueue.Enqueue(async (_, workCt) =>
            {
                using var scope = scopeFactory.CreateScope();
                var scan = scope.ServiceProvider.GetRequiredService<IScanAppService>();
                await scan.RunLineCompleteJobAsync(taskId, callbackUrl, workCt).ConfigureAwait(false);
            });
        }

        return Task.FromResult(DasOperationResult<AsyncAcceptedData>.Ok(new AsyncAcceptedData
        {
            TaskId = task.TaskId,
            Status = AsyncAcceptedData.StatusProcessing,
        }));
    }

    /// <inheritdoc />
    public async Task RunLineCompleteJobAsync(string taskId, string callbackUrl, CancellationToken ct = default)
    {
        if (!_lineTasks.TryGet(taskId, out var task) || task is null || task.Aborted)
        {
            return;
        }

        try
        {
            await _lineScanner.StopRecordingAsync(task.SessionDirectory, ct).ConfigureAwait(false);
            if (task.Aborted)
            {
                return;
            }

            var baseline = await _rollerCalibration.GetAsync(task.ScannerId, ct).ConfigureAwait(false);
            if (baseline is null)
            {
                await FinishLineFailedAsync(task, callbackUrl, "SCAN_LINE_003", "尚未完成滾輪校正，無法提供絕對高度。", ct)
                    .ConfigureAwait(false);
                return;
            }

            IReadOnlyList<MeasuredAngleIron> parts;
            if (string.Equals(_lineScan.MeasurementEngine, LineScanSettings.EngineCSharp, StringComparison.OrdinalIgnoreCase))
            {
                parts = _csharpMeasure.Measure(
                    task.SessionDirectory,
                    baseline,
                    task.RollerSpeed,
                    _lineScan.GapMm,
                    _lineScan.MinHeightMm,
                    _lineRegion.Get(),
                    _lineScan.AverageScans,
                    _lineScan.CropStartSeconds,
                    _lineScan.CropEndSeconds);
            }
            else if (string.Equals(_lineScan.MeasurementEngine, LineScanSettings.EngineHankPython, StringComparison.OrdinalIgnoreCase))
            {
                parts = await _lineScanner.MeasureAsync(
                    task.SessionDirectory,
                    baseline,
                    task.RollerSpeed,
                    _lineScan.GapMm,
                    _lineScan.MinHeightMm,
                    ct).ConfigureAwait(false);
            }
            else
            {
                await FinishLineFailedAsync(
                    task,
                    callbackUrl,
                    "SCAN_LINE_002",
                    "LineScan:MeasurementEngine 必須是 HankPython 或 CSharp。",
                    ct).ConfigureAwait(false);
                return;
            }
            if (task.Aborted)
            {
                return;
            }

            var result = new LineScanCompleteResult
            {
                DetectedCount = parts.Count,
                AngleIrons = parts.Select(ToAngleIron).ToList(),
            };
            task.IsCompleted = true;
            _lineTasks.Save(task);
            var payload = new TaskResultData
            {
                TaskId = task.TaskId,
                Status = TaskResultData.StatusCompleted,
                Result = result,
                Error = null,
                TraceId = task.TraceId,
            };
            _taskResults.SetLatestLine(payload);
            await _callback.NotifyAsync(callbackUrl, payload, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is InvalidOperationException or TimeoutException or IOException or JsonException)
        {
            if (task.Aborted)
            {
                return;
            }

            await FinishLineFailedAsync(task, callbackUrl, "SCAN_LINE_002", ex.Message, ct).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<DasOperationResult<ScanAbortData>> AbortLineScanAsync(
        LineScanAbortRequest request,
        CancellationToken ct = default)
    {
        if (!_lineTasks.TryGet(request.TaskId, out var task) || task is null || task.Aborted || task.IsCompleted)
        {
            return DasOperationResult<ScanAbortData>.Fail("SCAN_LINE_001", "taskId 不存在或已逾時。");
        }

        var traceId = Guid.NewGuid().ToString("N");
        task.Aborted = true;
        task.IsCompleted = true;
        _lineTasks.Save(task);
        await _lineScanner.AbortRecordingAsync(task.SessionDirectory, ct).ConfigureAwait(false);
        var payload = new TaskResultData
        {
            TaskId = task.TaskId,
            Status = TaskResultData.StatusAborted,
            Result = null,
            Error = null,
            TraceId = traceId,
        };
        _taskResults.Set(payload);
        return DasOperationResult<ScanAbortData>.Ok(new ScanAbortData
        {
            TaskId = task.TaskId,
            Status = ScanAbortData.StatusAborted,
            TraceId = traceId,
        });
    }

    /// <inheritdoc />
    public async Task ExpireLineScanAsync(string taskId, CancellationToken ct = default)
    {
        if (!_lineTasks.TryGet(taskId, out var task) || task is null || task.IsCompleted)
        {
            return;
        }

        task.Aborted = true;
        task.IsCompleted = true;
        await _lineScanner.AbortRecordingAsync(task.SessionDirectory, ct).ConfigureAwait(false);
        _taskResults.Remove(taskId);
        _lineTasks.Remove(taskId);
    }

    /// <inheritdoc />
    /// <remarks>
    /// <c>innerLeft</c>／<c>innerRight</c> 有值：依規格 v1.1 §9.8 量兩斜面厚度，寫入 <c>JobThickness</c>。
    /// 皆無：依 jobId 讀 <c>JobThickness</c> 組模型。
    /// <c>final=true</c> 且本任務已有兩段以上時，沿 X 拼接後回傳合併點雲／ZIP。
    /// 每次呼叫（含失敗）追加一筆 <c>ScanCallLog</c>。
    /// </remarks>
    public async Task<ApiResponse<SurfaceScanData>> ScanSurfaceAsync(
        SurfaceScanRequest request,
        CancellationToken ct = default)
    {
        var traceId = Guid.NewGuid().ToString("N");

        if (!_registry.TryGetEndpoint(request.ScannerId, out var endpoint))
        {
            return await SurfaceFailAsync(
                request, traceId, "SCAN_CFG_001", $"未知 ScannerId: {request.ScannerId}", ct)
                .ConfigureAwait(false);
        }

        SurfaceScanTaskRecord task;
        if (string.IsNullOrWhiteSpace(request.TaskId))
        {
            task = _taskService.Create(request.ScannerId);
        }
        else
        {
            if (!_taskService.TryGet(request.TaskId.Trim(), out var existing) || existing is null)
            {
                return await SurfaceFailAsync(
                    request, traceId, "SCAN_001", "找不到對應的面掃描任務。", ct)
                    .ConfigureAwait(false);
            }

            if (!string.Equals(existing.ScannerId, request.ScannerId, StringComparison.OrdinalIgnoreCase))
            {
                return await SurfaceFailAsync(
                    request, traceId, "SCAN_001", "scannerId 與既有任務不符。", ct)
                    .ConfigureAwait(false);
            }

            if (existing.IsCompleted)
            {
                return await SurfaceFailAsync(
                    request, traceId, "SCAN_001", "任務已結束，請勿再呼叫。", ct)
                    .ConfigureAwait(false);
            }

            task = existing;
        }

        try
        {
            ThicknessMeasureRequest? thickness = null;
            JobThicknessRecord? storedThickness = null;
            if (HasInnerSurfaces(request))
            {
                thickness = ToThicknessRequest(request);
            }
            else
            {
                storedThickness = await _jobThickness.GetByJobIdAsync(request.JobId, ct)
                    .ConfigureAwait(false);
                if (storedThickness is null)
                {
                    return await SurfaceFailAsync(
                        request,
                        traceId,
                        "SCAN_THK_002",
                        $"找不到工單 {request.JobId.Trim()} 的噴印厚度，請先於噴印區量測。",
                        ct,
                        task.TaskId)
                        .ConfigureAwait(false);
                }
            }

            SurfaceScanPipelineResult processed;
            if (_surfaceScan.UseExistingPly)
            {
                if (!TryCopyExistingPly(
                    _surfaceScan.ExistingPlyPath,
                    request.ScannerId,
                    out var destRawPly,
                    out var outputDir,
                    out var plyError))
                {
                    return await SurfaceFailAsync(
                        request, traceId, "SCAN_001", plyError, ct, task.TaskId)
                        .ConfigureAwait(false);
                }

                processed = await _pipeline.ProcessRawPlyAsync(
                        request.ScannerId,
                        destRawPly,
                        outputDir,
                        ct,
                        thickness,
                        storedThickness,
                        ToCameraPosition(request.CameraPosition))
                    .ConfigureAwait(false);
            }
            else
            {
                var programNo = _registry.ResolveProgramNo(request.ScannerId, endpoint.NavigatorScanNo);
                var capture = await _captureFactory.GetCapture(request.ScannerId)
                    .CaptureAsync(programNo, ct)
                    .ConfigureAwait(false);
                processed = await _pipeline.ProcessCaptureAsync(
                        capture,
                        ct,
                        thickness,
                        storedThickness,
                        ToCameraPosition(request.CameraPosition))
                    .ConfigureAwait(false);
            }

            return await CompleteSurfaceScanAsync(
                    request,
                    task,
                    traceId,
                    processed,
                    storedThickness,
                    ScanCallLogActions.ScanSurface,
                    ct)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var code = "SCAN_SDK_001";
            var message = ex.Message;
            if (HasInnerSurfaces(request)
                && ex.Message.StartsWith("厚度量測", StringComparison.Ordinal))
            {
                code = "SCAN_THK_001";
            }

            return await SurfaceFailAsync(request, traceId, code, message, ct, task.TaskId)
                .ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// 不連掃描器、不載入 LJ SDK。讀既有 <c>raw_scene.ply</c> 後，厚度、任務、拼接、ZIP／PLY／JSON／GLB／PNG 與 <see cref="ScanSurfaceAsync"/> 相同。
    /// </remarks>
    public async Task<ApiResponse<SurfaceScanData>> ScanSurfaceFromFileAsync(
        SurfaceFromFileRequest request,
        CancellationToken ct = default)
    {
        var traceId = Guid.NewGuid().ToString("N");
        var logAction = ScanCallLogActions.ScanSurfaceFromFile;
        var surfaceRequest = request.ToSurfaceScanRequest();

        if (!_registry.TryGetEndpoint(request.ScannerId, out var endpoint))
        {
            return await SurfaceFailAsync(
                surfaceRequest, traceId, "SCAN_CFG_001", $"未知 ScannerId: {request.ScannerId}", ct, logAction: logAction)
                .ConfigureAwait(false);
        }

        SurfaceScanTaskRecord task;
        if (string.IsNullOrWhiteSpace(request.TaskId))
        {
            task = _taskService.Create(request.ScannerId);
        }
        else
        {
            if (!_taskService.TryGet(request.TaskId.Trim(), out var existing) || existing is null)
            {
                return await SurfaceFailAsync(
                    surfaceRequest, traceId, "SCAN_001", "找不到對應的面掃描任務。", ct, logAction: logAction)
                    .ConfigureAwait(false);
            }

            if (!string.Equals(existing.ScannerId, request.ScannerId, StringComparison.OrdinalIgnoreCase))
            {
                return await SurfaceFailAsync(
                    surfaceRequest, traceId, "SCAN_001", "scannerId 與既有任務不符。", ct, logAction: logAction)
                    .ConfigureAwait(false);
            }

            if (existing.IsCompleted)
            {
                return await SurfaceFailAsync(
                    surfaceRequest, traceId, "SCAN_001", "任務已結束，請勿再呼叫。", ct, logAction: logAction)
                    .ConfigureAwait(false);
            }

            task = existing;
        }

        try
        {
            ThicknessMeasureRequest? thickness = null;
            JobThicknessRecord? storedThickness = null;
            if (HasInnerSurfaces(surfaceRequest))
            {
                thickness = ToThicknessRequest(surfaceRequest);
            }
            else
            {
                storedThickness = await _jobThickness.GetByJobIdAsync(request.JobId, ct)
                    .ConfigureAwait(false);
                if (storedThickness is null)
                {
                    return await SurfaceFailAsync(
                        surfaceRequest,
                        traceId,
                        "SCAN_THK_002",
                        $"找不到工單 {request.JobId.Trim()} 的噴印厚度，請先於噴印區量測。",
                        ct,
                        task.TaskId,
                        logAction: logAction)
                        .ConfigureAwait(false);
                }
            }

            var sourceDir = request.CaptureDirectory;
            if (string.IsNullOrWhiteSpace(sourceDir))
            {
                sourceDir = endpoint.OfflineCaptureDirectory;
            }

            if (string.IsNullOrWhiteSpace(sourceDir))
            {
                return await SurfaceFailAsync(
                    surfaceRequest,
                    traceId,
                    "SCAN_001",
                    "請指定 captureDirectory，或在 Scanners 設定 OfflineCaptureDirectory。",
                    ct,
                    task.TaskId,
                    logAction: logAction)
                    .ConfigureAwait(false);
            }

            sourceDir = Path.GetFullPath(sourceDir.Trim());
            var sourceRawPly = Path.Combine(sourceDir, "raw_scene.ply");
            if (!TryCopyExistingPly(
                sourceRawPly,
                request.ScannerId,
                out var destRawPly,
                out var outputDir,
                out var plyError))
            {
                return await SurfaceFailAsync(
                    surfaceRequest,
                    traceId,
                    "SCAN_001",
                    plyError,
                    ct,
                    task.TaskId,
                    logAction: logAction)
                    .ConfigureAwait(false);
            }

            var processed = await _pipeline.ProcessRawPlyAsync(
                    request.ScannerId,
                    destRawPly,
                    outputDir,
                    ct,
                    thickness,
                    storedThickness,
                    ToCameraPosition(request.CameraPosition))
                .ConfigureAwait(false);
            return await CompleteSurfaceScanAsync(
                    surfaceRequest,
                    task,
                    traceId,
                    processed,
                    storedThickness,
                    logAction,
                    ct)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var code = "SCAN_001";
            var message = ex.Message;
            if (HasInnerSurfaces(surfaceRequest)
                && ex.Message.StartsWith("厚度量測", StringComparison.Ordinal))
            {
                code = "SCAN_THK_001";
            }

            return await SurfaceFailAsync(
                surfaceRequest, traceId, code, message, ct, task.TaskId, logAction: logAction)
                .ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<DasOperationResult<SurfaceScanStartData>> StartSurfaceScanAsync(
        SurfaceScanStartRequest request,
        CancellationToken ct = default)
    {
        var mapped = new SurfaceScanRequest
        {
            ScannerId = request.ScannerId,
            JobId = request.JobId,
            TaskId = null,
            Final = false,
            CameraPosition = ToCoordinate(request.CameraPosition),
            InnerLeft = request.InnerLeft,
            InnerRight = request.InnerRight,
        };
        var api = await ScanSurfaceAsync(mapped, ct).ConfigureAwait(false);
        if (!api.Success || api.Data is null)
        {
            return ToDasFail<SurfaceScanStartData>(api);
        }

        var data = api.Data;
        RememberProcessing(data.TaskId, data.TraceId);
        return DasOperationResult<SurfaceScanStartData>.Ok(new SurfaceScanStartData
        {
            TaskId = data.TaskId,
            PointCloudUrl = data.PointCloudUrl,
            HeadFeatures = data.HeadFeatures ?? new CornerTripletDto(),
            SegmentZDelta = data.SegmentZDelta,
            ThicknessLeft = data.ThicknessLeft,
            ThicknessRight = data.ThicknessRight,
            TraceId = data.TraceId,
        });
    }

    /// <inheritdoc />
    public async Task<DasOperationResult<SurfaceScanSegmentData>> SegmentSurfaceScanAsync(
        SurfaceScanSegmentRequest request,
        CancellationToken ct = default)
    {
        if (!_taskService.TryGet(request.TaskId, out var task) || task is null)
        {
            return DasOperationResult<SurfaceScanSegmentData>.Fail("SURF_001", "找不到對應的面掃描任務。");
        }

        var mapped = new SurfaceScanRequest
        {
            ScannerId = task.ScannerId,
            JobId = task.JobId,
            TaskId = request.TaskId,
            Final = false,
            CameraPosition = ToCoordinate(request.CameraPosition),
        };
        var api = await ScanSurfaceAsync(mapped, ct).ConfigureAwait(false);
        if (!api.Success || api.Data is null)
        {
            return ToDasFail<SurfaceScanSegmentData>(api);
        }

        var data = api.Data;
        RememberProcessing(data.TaskId, data.TraceId);
        return DasOperationResult<SurfaceScanSegmentData>.Ok(new SurfaceScanSegmentData
        {
            PointCloudUrl = data.PointCloudUrl,
            HeadFeatures = data.HeadFeatures ?? new CornerTripletDto(),
            SegmentZDelta = data.SegmentZDelta,
            TraceId = data.TraceId,
        });
    }

    /// <inheritdoc />
    public Task<DasOperationResult<AsyncAcceptedData>> CompleteSurfaceScanAsync(
        SurfaceScanCompleteRequest request,
        CancellationToken ct = default)
    {
        _ = ct;
        var taskId = request.TaskId.Trim();
        if (!_taskService.TryGet(taskId, out var task) || task is null)
        {
            return Task.FromResult(
                DasOperationResult<AsyncAcceptedData>.Fail("SURF_001", "找不到對應的面掃描任務。"));
        }

        if (_taskResults.TryGet(taskId, out var stored) && stored is not null
            && stored.Status == TaskResultData.StatusAborted)
        {
            return Task.FromResult(
                DasOperationResult<AsyncAcceptedData>.Fail("SURF_001", "任務已結束，請勿再呼叫。"));
        }

        if (task.IsCompleted
            && stored is not null
            && stored.Status == TaskResultData.StatusCompleted)
        {
            var callbackUrlRepeat = request.CallbackUrl;
            var scopeFactoryRepeat = _scopeFactory;
            _workQueue.Enqueue(async (_, workCt) =>
            {
                using var scope = scopeFactoryRepeat.CreateScope();
                var scan = scope.ServiceProvider.GetRequiredService<IScanAppService>();
                await scan.RunSurfaceCompleteJobAsync(taskId, callbackUrlRepeat, workCt).ConfigureAwait(false);
            });
            return Task.FromResult(DasOperationResult<AsyncAcceptedData>.Ok(new AsyncAcceptedData
            {
                TaskId = task.TaskId,
                Status = AsyncAcceptedData.StatusProcessing,
            }));
        }

        if (task.IsCompleted)
        {
            return Task.FromResult(
                DasOperationResult<AsyncAcceptedData>.Fail("SURF_001", "任務已結束，請勿再呼叫。"));
        }

        RememberProcessing(task.TaskId, Guid.NewGuid().ToString("N"));
        var callbackUrl = request.CallbackUrl;
        var scopeFactory = _scopeFactory;
        _workQueue.Enqueue(async (_, workCt) =>
        {
            using var scope = scopeFactory.CreateScope();
            var scan = scope.ServiceProvider.GetRequiredService<IScanAppService>();
            await scan.RunSurfaceCompleteJobAsync(taskId, callbackUrl, workCt).ConfigureAwait(false);
        });

        return Task.FromResult(DasOperationResult<AsyncAcceptedData>.Ok(new AsyncAcceptedData
        {
            TaskId = task.TaskId,
            Status = AsyncAcceptedData.StatusProcessing,
        }));
    }

    /// <inheritdoc />
    public async Task RunSurfaceCompleteJobAsync(string taskId, string callbackUrl, CancellationToken ct = default)
    {
        var finish = await FinalizeSurfaceTaskAsync(taskId, ct).ConfigureAwait(false);
        TaskResultData payload;
        if (_taskResults.TryGet(taskId, out var stored) && stored is not null
            && (stored.Status == TaskResultData.StatusCompleted || stored.Status == TaskResultData.StatusFailed))
        {
            payload = stored;
        }
        else if (finish.Success && finish.Data is not null)
        {
            payload = new TaskResultData
            {
                TaskId = taskId.Trim(),
                Status = TaskResultData.StatusCompleted,
                Result = finish.Data,
                Error = null,
                TraceId = "",
            };
            _taskResults.Set(payload);
        }
        else
        {
            payload = new TaskResultData
            {
                TaskId = taskId.Trim(),
                Status = TaskResultData.StatusFailed,
                Result = null,
                Error = finish.Error ?? new TaskErrorDto
                {
                    Code = "SURF_001",
                    Message = "結束面掃描失敗。",
                },
                TraceId = "",
            };
            _taskResults.Set(payload);
        }

        await _callback.NotifyAsync(callbackUrl, payload, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<DasOperationResult<ScanAbortData>> AbortSurfaceScanAsync(
        SurfaceScanAbortRequest request,
        CancellationToken ct = default)
    {
        _ = ct;
        if (!_taskService.TryGet(request.TaskId, out var task) || task is null)
        {
            return Task.FromResult(
                DasOperationResult<ScanAbortData>.Fail("SURF_001", "找不到對應的面掃描任務。"));
        }

        if (task.IsCompleted)
        {
            return Task.FromResult(
                DasOperationResult<ScanAbortData>.Fail("SURF_001", "任務已結束，請勿再呼叫。"));
        }

        var traceId = Guid.NewGuid().ToString("N");
        task.IsCompleted = true;
        task.UpdatedAt = DateTimeOffset.Now;
        _taskService.Save(task);
        _taskResults.Set(new TaskResultData
        {
            TaskId = task.TaskId,
            Status = TaskResultData.StatusAborted,
            Result = null,
            Error = null,
            TraceId = traceId,
        });
        return Task.FromResult(DasOperationResult<ScanAbortData>.Ok(new ScanAbortData
        {
            TaskId = task.TaskId,
            Status = ScanAbortData.StatusAborted,
            TraceId = traceId,
        }));
    }

    /// <inheritdoc />
    /// <remarks>
    /// 成功時 Controller 以實體 ZIP 回應。寫入 <c>ScanCallLog</c>（含 ZIP SHA256）。
    /// </remarks>
    public async Task<ApiResponse<SurfaceScanAttachmentFile>> GetSurfaceAttachmentsAsync(
        string taskId,
        CancellationToken ct = default)
    {
        var traceId = Guid.NewGuid().ToString("N");
        var requestJson = Serialize(new { taskId }) ?? "{}";

        if (string.IsNullOrWhiteSpace(taskId))
        {
            var missing = ApiResponse<SurfaceScanAttachmentFile>.Fail("SCAN_001", "taskId 必填。");
            await TryAppendScanLogAsync(
                BuildAttachmentLog(traceId, null, requestJson, missing, null),
                ct).ConfigureAwait(false);
            return missing;
        }

        if (!_taskService.TryGet(taskId.Trim(), out var task) || task is null)
        {
            var notFound = ApiResponse<SurfaceScanAttachmentFile>.Fail("SCAN_001", "找不到對應的面掃描任務。");
            await TryAppendScanLogAsync(
                BuildAttachmentLog(traceId, taskId, requestJson, notFound, null),
                ct).ConfigureAwait(false);
            return notFound;
        }

        if (string.IsNullOrWhiteSpace(task.ZipPath) || !File.Exists(task.ZipPath))
        {
            var noFile = ApiResponse<SurfaceScanAttachmentFile>.Fail("SCAN_001", "此任務尚無附加檔。");
            await TryAppendScanLogAsync(
                BuildAttachmentLog(traceId, task.TaskId, requestJson, noFile, null),
                ct).ConfigureAwait(false);
            return noFile;
        }

        var file = new SurfaceScanAttachmentFile
        {
            ZipFullPath = task.ZipPath,
            DownloadFileName = Path.GetFileName(task.ZipPath),
        };
        var ok = ApiResponse<SurfaceScanAttachmentFile>.Ok(file);
        await TryAppendScanLogAsync(
            BuildAttachmentLog(traceId, task.TaskId, requestJson, ok, task.ZipPath),
            ct).ConfigureAwait(false);
        return ok;
    }

    /// <inheritdoc />
    /// <remarks>孔洞掃描尚未實作，固定回 501 <c>SYS_NYI</c>，仍寫入 <c>ScanCallLog</c>。</remarks>
    public async Task<ApiResponse<HoleScanData>> ScanHoleAsync(
        HoleScanRequest request,
        CancellationToken ct = default)
    {
        var response = SpecNotImplemented.Response<HoleScanData>("POST /api/scan/hole");
        await TryAppendScanLogAsync(
            new ScanCallLogRecord
            {
                Timestamp = DateTimeOffset.Now,
                TraceId = Guid.NewGuid().ToString("N"),
                Action = ScanCallLogActions.ScanHole,
                ScannerId = request.ScannerId,
                Success = false,
                ErrorCode = response.ErrorCode,
                ErrorMessage = response.ErrorMessage,
                RequestJson = Serialize(request),
                ResponseJson = Serialize(response),
            },
            ct).ConfigureAwait(false);
        return response;
    }

    /// <inheritdoc />
    public Task<DasOperationResult<TaskResultData>> GetTaskResultAsync(
        string taskId,
        CancellationToken ct = default)
    {
        _ = ct;
        if (string.IsNullOrWhiteSpace(taskId))
        {
            return Task.FromResult(
                DasOperationResult<TaskResultData>.Fail("SURF_001", "taskId 必填。"));
        }

        if (_taskResults.TryGet(taskId, out var stored) && stored is not null)
        {
            return Task.FromResult(DasOperationResult<TaskResultData>.Ok(stored));
        }

        if (_taskService.TryGet(taskId, out var task) && task is not null)
        {
            if (task.IsCompleted && !string.IsNullOrWhiteSpace(task.PointCloudPath))
            {
                return Task.FromResult(DasOperationResult<TaskResultData>.Ok(new TaskResultData
                {
                    TaskId = task.TaskId,
                    Status = TaskResultData.StatusCompleted,
                    Result = new SurfaceScanCompleteResult
                    {
                        PointCloudUrl = ToScanFileUrl(task.PointCloudPath),
                    },
                    Error = null,
                    TraceId = "",
                }));
            }

            return Task.FromResult(DasOperationResult<TaskResultData>.Ok(new TaskResultData
            {
                TaskId = task.TaskId,
                Status = TaskResultData.StatusProcessing,
                Result = null,
                Error = null,
                TraceId = "",
            }));
        }

        return Task.FromResult(
            DasOperationResult<TaskResultData>.Fail("SURF_001", "找不到對應的任務。"));
    }

    /// <summary>
    /// 把匯出根目錄下的絕對路徑轉成 API 靜態檔 URL（<c>/scan-files/...</c>）。
    /// 若檔案不在 ExportRoot 內，則回傳原絕對路徑。
    /// </summary>
    /// <param name="fullPath">點雲或其他匯出檔的絕對路徑。</param>
    /// <returns>可供台船下載的相對 URL 或原始路徑。</returns>
    private string ToScanFileUrl(string fullPath)
    {
        var relative = Path.GetRelativePath(_storage.ExportRoot, fullPath);
        if (string.IsNullOrWhiteSpace(relative) || relative.StartsWith("..", StringComparison.Ordinal))
        {
            return fullPath;
        }

        return "/scan-files/" + relative.Replace('\\', '/');
    }

    /// <summary>
    /// 指南 complete：不再拍攝，把已掃段落標記完成；兩段以上才拼接。
    /// </summary>
    private async Task<DasOperationResult<SurfaceScanCompleteResult>> FinalizeSurfaceTaskAsync(
        string taskId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(taskId) || !_taskService.TryGet(taskId, out var task) || task is null)
        {
            return DasOperationResult<SurfaceScanCompleteResult>.Fail("SURF_001", "找不到對應的面掃描任務。");
        }

        var traceId = Guid.NewGuid().ToString("N");
        if (task.IsCompleted)
        {
            if (string.IsNullOrWhiteSpace(task.PointCloudPath))
            {
                return DasOperationResult<SurfaceScanCompleteResult>.Fail("SURF_001", "任務已結束，請勿再呼叫。");
            }

            var existing = new SurfaceScanCompleteResult
            {
                PointCloudUrl = ToScanFileUrl(task.PointCloudPath),
            };
            RememberCompleted(task.TaskId, existing, traceId);
            return DasOperationResult<SurfaceScanCompleteResult>.Ok(existing);
        }

        if (task.SegmentPlyPaths is null || task.SegmentPlyPaths.Count == 0)
        {
            return DasOperationResult<SurfaceScanCompleteResult>.Fail("SURF_001", "此任務尚無掃描段落。");
        }

        try
        {
            var resultPly = task.PointCloudPath;
            var resultZip = task.ZipPath;
            if (task.SegmentPlyPaths.Count >= 2)
            {
                JobThicknessRecord? meshThickness = null;
                if (!string.IsNullOrWhiteSpace(task.JobId))
                {
                    meshThickness = await _jobThickness.GetByJobIdAsync(task.JobId, ct)
                        .ConfigureAwait(false);
                }

                var taskDir = _taskService.EnsureDirectory(task.TaskId);
                var stitched = await _pipeline.ExportStitchedAsync(
                    task.SegmentPlyPaths,
                    taskDir,
                    ct,
                    meshThickness).ConfigureAwait(false);
                resultPly = stitched.PointCloudPath;
                resultZip = stitched.ZipPath;
                if (!string.IsNullOrWhiteSpace(stitched.GlbPath))
                {
                    PublishMonitorGlb(stitched.GlbPath, task.ScannerId);
                }
            }

            if (string.IsNullOrWhiteSpace(resultPly))
            {
                return DasOperationResult<SurfaceScanCompleteResult>.Fail("SURF_001", "此任務沒有點雲。");
            }

            task.PointCloudPath = resultPly;
            task.ZipPath = resultZip;
            task.IsCompleted = true;
            task.UpdatedAt = DateTimeOffset.Now;
            _taskService.Save(task);

            var complete = new SurfaceScanCompleteResult
            {
                PointCloudUrl = ToScanFileUrl(resultPly),
            };
            RememberCompleted(task.TaskId, complete, traceId);
            return DasOperationResult<SurfaceScanCompleteResult>.Ok(complete);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return DasOperationResult<SurfaceScanCompleteResult>.Fail("SCAN_001", ex.Message);
        }
    }

    /// <summary>把舊 API 失敗轉成指南 error.code。</summary>
    private static DasOperationResult<T> ToDasFail<T>(ApiResponse<SurfaceScanData> api)
    {
        var code = api.ErrorCode ?? "SCAN_001";
        if (string.Equals(code, "SCAN_001", StringComparison.Ordinal))
        {
            code = "SURF_001";
        }

        return DasOperationResult<T>.Fail(code, api.ErrorMessage ?? "面掃描失敗。");
    }

    /// <summary>Pose 只取 x/y/z，a/c 本階段不進管線。</summary>
    private static CoordinateDto? ToCoordinate(CameraPoseDto? pose)
    {
        if (pose is null)
        {
            return null;
        }

        return new CoordinateDto
        {
            X = pose.X,
            Y = pose.Y,
            Z = pose.Z,
        };
    }

    /// <summary>start／segment 後供 DAS 輪詢，狀態為 Processing。</summary>
    private async Task FinishLineFailedAsync(
        LineScanTaskRecord task,
        string callbackUrl,
        string code,
        string message,
        CancellationToken ct)
    {
        task.IsCompleted = true;
        _lineTasks.Save(task);
        var payload = new TaskResultData
        {
            TaskId = task.TaskId,
            Status = TaskResultData.StatusFailed,
            Result = null,
            Error = new TaskErrorDto
            {
                Code = code,
                Message = message,
            },
            TraceId = task.TraceId,
        };
        _taskResults.SetLatestLine(payload);
        await _callback.NotifyAsync(callbackUrl, payload, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public TaskResultData GetLatestLineScanResult()
    {
        if (_taskResults.TryGetLatestLine(out var data) && data is not null)
        {
            return data;
        }

        return new TaskResultData
        {
            Status = "None",
        };
    }

    /// <inheritdoc />
    public LineScanRegion? GetLineRegion()
    {
        return _lineRegion.Get();
    }

    /// <inheritdoc />
    public void SetLineRegion(LineScanRegion? region)
    {
        _lineRegion.Save(region);
    }

    /// <inheritdoc />
    public LineScanLiveFrame GetLiveFrame()
    {
        LineScanTaskRecord? active = null;
        foreach (var task in _lineTasks.ListIncomplete())
        {
            if (task.Aborted || task.IsCompleted || task.Completing)
            {
                continue;
            }

            if (active is null || task.StartedAt > active.StartedAt)
            {
                active = task;
            }
        }

        if (active is null)
        {
            return new LineScanLiveFrame();
        }

        var (xs, ys) = ReadLatestFrame(Path.Combine(active.SessionDirectory, "frames.jsonl"));
        return new LineScanLiveFrame
        {
            Recording = true,
            TaskId = active.TaskId,
            X = xs,
            Y = ys,
        };
    }

    private static (double[] X, double[] Y) ReadLatestFrame(string path)
    {
        if (!File.Exists(path))
        {
            return ([], []);
        }

        string? line;
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            if (stream.Length == 0)
            {
                return ([], []);
            }

            var take = (int)Math.Min(stream.Length, 512 * 1024);
            stream.Seek(-take, SeekOrigin.End);
            var bytes = new byte[take];
            stream.ReadExactly(bytes);
            var text = System.Text.Encoding.UTF8.GetString(bytes);
            var last = text.LastIndexOf('\n');
            var previous = last > 0 ? text.LastIndexOf('\n', last - 1) : -1;
            if (last <= previous)
            {
                return ([], []);
            }

            line = text.Substring(previous + 1, last - previous - 1).Trim();
            if (line.Length == 0 || !line.StartsWith('{'))
            {
                return ([], []);
            }
        }

        using var doc = JsonDocument.Parse(line);
        if (!doc.RootElement.TryGetProperty("points", out var points) || points.ValueKind != JsonValueKind.Array)
        {
            return ([], []);
        }

        var xs = new List<double>();
        var ys = new List<double>();
        foreach (var point in points.EnumerateArray())
        {
            if (point.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            double x;
            double y;
            if (point.GetArrayLength() >= 3)
            {
                if (point[1].ValueKind != JsonValueKind.Number || point[2].ValueKind != JsonValueKind.Number)
                {
                    continue;
                }

                x = point[1].GetDouble();
                y = point[2].GetDouble();
            }
            else if (point.GetArrayLength() >= 2
                && point[0].ValueKind == JsonValueKind.Number
                && point[1].ValueKind == JsonValueKind.Number)
            {
                x = point[0].GetDouble();
                y = point[1].GetDouble();
            }
            else
            {
                continue;
            }

            if (!double.IsFinite(x) || !double.IsFinite(y))
            {
                continue;
            }

            xs.Add(x);
            ys.Add(y);
        }

        return (xs.ToArray(), ys.ToArray());
    }

    private static LineScanAngleIronDto ToAngleIron(MeasuredAngleIron part)
    {
        return new LineScanAngleIronDto
        {
            Index = part.Index,
            HeadFeatures = new CornerTripletDto
            {
                LeftCorner = ToLinePoint(part.HeadLeft),
                RightCorner = ToLinePoint(part.HeadRight),
                Apex = ToLinePoint(part.HeadApex),
            },
            TailFeatures = new CornerTripletDto
            {
                LeftCorner = ToLinePoint(part.TailLeft),
                RightCorner = ToLinePoint(part.TailRight),
                Apex = ToLinePoint(part.TailApex),
            },
            Width = part.Width,
            Height = part.Height,
            LeftLength = part.LeftLength,
            RightLength = part.RightLength,
            Length = part.Length,
            LeftWidth = part.LeftWidth,
            RightWidth = part.RightWidth,
        };
    }

    private static CoordinateDto ToLinePoint(MeasuredPoint point)
    {
        return new CoordinateDto
        {
            X = point.X,
            Y = point.Y,
            Z = point.Z,
        };
    }

    private void RememberProcessing(string taskId, string traceId)
    {
        _taskResults.Set(new TaskResultData
        {
            TaskId = taskId,
            Status = TaskResultData.StatusProcessing,
            Result = null,
            Error = null,
            TraceId = traceId,
        });
    }

    /// <summary>complete 成功後寫入點雲 URL。</summary>
    private void RememberCompleted(string taskId, SurfaceScanCompleteResult result, string traceId)
    {
        _taskResults.Set(new TaskResultData
        {
            TaskId = taskId,
            Status = TaskResultData.StatusCompleted,
            Result = result,
            Error = null,
            TraceId = traceId,
        });
    }

    /// <summary>
    /// 把 API 的 cameraPosition 轉成管線機台座標；未傳則 null（視為原點）。
    /// </summary>
    private static CameraPosition? ToCameraPosition(CoordinateDto? dto)
    {
        if (dto is null)
        {
            return null;
        }

        return new CameraPosition
        {
            X = dto.X,
            Y = dto.Y,
            Z = dto.Z,
        };
    }

    /// <summary>
    /// 本次請求是否帶測距儀內表面點（規格 innerLeft／innerRight）。
    /// </summary>
    private static bool HasInnerSurfaces(SurfaceScanRequest request)
    {
        return request.InnerLeft is not null || request.InnerRight is not null;
    }

    /// <summary>
    /// 把 API 的 innerLeft／innerRight 轉成管線量測輸入。
    /// </summary>
    private static ThicknessMeasureRequest ToThicknessRequest(SurfaceScanRequest request)
    {
        var left = request.InnerLeft;
        var right = request.InnerRight;
        return new ThicknessMeasureRequest
        {
            HasInnerLeft = left is not null,
            InnerLeftX = left is null ? 0 : left.X,
            InnerLeftY = left is null ? 0 : left.Y,
            InnerLeftZ = left is null ? 0 : left.Z,
            HasInnerRight = right is not null,
            InnerRightX = right is null ? 0 : right.X,
            InnerRightY = right is null ? 0 : right.Y,
            InnerRightZ = right is null ? 0 : right.Z,
        };
    }

    /// <summary>
    /// 面掃描在 Pipeline 產出之後的共同步驟：厚度寫入、分段、可選拼接、任務存檔、回傳。
    /// </summary>
    private async Task<ApiResponse<SurfaceScanData>> CompleteSurfaceScanAsync(
        SurfaceScanRequest request,
        SurfaceScanTaskRecord task,
        string traceId,
        SurfaceScanPipelineResult processed,
        JobThicknessRecord? storedThickness,
        string logAction,
        CancellationToken ct)
    {
        if (HasInnerSurfaces(request)
            && processed.ThicknessA is not null
            && processed.ThicknessB is not null)
        {
            await _jobThickness.UpsertAsync(
                new JobThicknessRecord
                {
                    JobId = request.JobId.Trim(),
                    ThicknessA = processed.ThicknessA.Value,
                    ThicknessB = processed.ThicknessB.Value,
                    ThicknessAOnLowFlange = processed.ThicknessAOnLowFlange ?? true,
                    ScannerId = request.ScannerId,
                    MeasuredAt = DateTimeOffset.Now,
                    TraceId = traceId,
                },
                ct).ConfigureAwait(false);
        }

        var seq = task.Seq + 1;
        double zDelta = 0;
        if (processed.HeadFeatures is not null)
        {
            zDelta = processed.HeadFeatures.RidgeZDelta;
        }

        var taskDir = _taskService.EnsureDirectory(task.TaskId);
        var segmentPlyPath = Path.Combine(taskDir, $"segment_{seq:D3}.ply");
        File.Copy(processed.PointCloudPath, segmentPlyPath, overwrite: true);
        if (task.SegmentPlyPaths is null)
        {
            task.SegmentPlyPaths = new List<string>();
        }

        task.SegmentPlyPaths.Add(segmentPlyPath);

        var resultPly = processed.PointCloudPath;
        var resultZip = processed.ZipPath;
        var previewGlb = processed.GlbPath;
        if (request.Final && task.SegmentPlyPaths.Count >= 2)
        {
            JobThicknessRecord? meshThickness = storedThickness;
            if (processed.ThicknessA is not null && processed.ThicknessB is not null)
            {
                meshThickness = new JobThicknessRecord
                {
                    JobId = request.JobId.Trim(),
                    ThicknessA = processed.ThicknessA.Value,
                    ThicknessB = processed.ThicknessB.Value,
                    ThicknessAOnLowFlange = processed.ThicknessAOnLowFlange ?? true,
                    ScannerId = request.ScannerId,
                    MeasuredAt = DateTimeOffset.Now,
                    TraceId = traceId,
                };
            }

            var stitched = await _pipeline.ExportStitchedAsync(
                task.SegmentPlyPaths,
                taskDir,
                ct,
                meshThickness).ConfigureAwait(false);
            resultPly = stitched.PointCloudPath;
            resultZip = stitched.ZipPath;
            if (!string.IsNullOrWhiteSpace(stitched.GlbPath))
            {
                previewGlb = stitched.GlbPath;
            }
        }

        task.Seq = seq;
        task.JobId = request.JobId;
        task.ZipPath = resultZip;
        task.PointCloudPath = resultPly;
        task.LastMeanZ = processed.MeanZ;
        task.IsCompleted = request.Final;
        task.UpdatedAt = DateTimeOffset.Now;
        _taskService.Save(task);
        PublishMonitorGlb(previewGlb, request.ScannerId);

        var data = new SurfaceScanData
        {
            TaskId = task.TaskId,
            Seq = seq,
            Status = request.Final ? SurfaceScanData.StatusFinal : SurfaceScanData.StatusContinue,
            PointCloudUrl = ToScanFileUrl(resultPly),
            ThicknessLeft = HasInnerSurfaces(request) ? processed.ThicknessA : null,
            ThicknessRight = HasInnerSurfaces(request) ? processed.ThicknessB : null,
            SegmentZDelta = zDelta,
            HeadFeatures = ToCornerTriplet(processed.HeadFeatures),
            HeadFeaturesKind = processed.HeadFeatures?.Kind,
            CompensationApplied = processed.CompensationApplied,
            TraceId = traceId,
        };
        var ok = ApiResponse<SurfaceScanData>.Ok(data);
        await TryAppendScanLogAsync(
            BuildSurfaceLog(
                request,
                traceId,
                ok,
                task.TaskId,
                seq,
                resultPly,
                resultZip,
                logAction),
            ct).ConfigureAwait(false);
        return ok;
    }

    /// <summary>將來源 ply 複製到本次匯出目錄的 <c>raw_scene.ply</c>。</summary>
    /// <param name="sourcePly">來源 ply 絕對或相對路徑。</param>
    /// <param name="scannerId">掃描器編號（輸出資料夾用）。</param>
    /// <param name="destRawPly">複製後的 raw_scene.ply。</param>
    /// <param name="outputDir">新建的輸出目錄。</param>
    /// <param name="error">失敗說明。</param>
    /// <returns>成功為 true。</returns>
    private bool TryCopyExistingPly(
        string? sourcePly,
        string scannerId,
        out string destRawPly,
        out string outputDir,
        out string error)
    {
        destRawPly = "";
        outputDir = "";
        error = "";
        if (string.IsNullOrWhiteSpace(sourcePly))
        {
            error = "UseExistingPly 為 true 但 ExistingPlyPath 未設定。";
            return false;
        }

        var full = Path.GetFullPath(sourcePly.Trim());
        if (!File.Exists(full))
        {
            error = $"找不到既有 ply：{full}";
            return false;
        }

        outputDir = CreateOfflineOutputDirectory(scannerId);
        destRawPly = Path.Combine(outputDir, "raw_scene.ply");
        File.Copy(full, destRawPly, overwrite: true);
        return true;
    }

    /// <summary>
    /// 離線處理輸出目錄，格式與現場擷取資料夾相同（掃描器／時間戳＿短 Id）。
    /// </summary>
    /// <param name="scannerId">掃描器編號。</param>
    /// <returns>新建的絕對路徑。</returns>
    private string CreateOfflineOutputDirectory(string scannerId)
    {
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var shortId = Guid.NewGuid().ToString("N").Substring(0, 8);
        var folder = Path.Combine(_storage.ExportRoot, scannerId, $"{stamp}_{shortId}");
        Directory.CreateDirectory(folder);
        return folder;
    }

    /// <summary>
    /// 把最新 GLB 路徑寫到 ContentsRoot，給對應產線區域的監看畫面自動載入。
    /// </summary>
    /// <param name="glbPath">GLB 絕對路徑；缺檔則略過。</param>
    /// <param name="scannerId">用來對 appsettings MachineArea（printing / cutting）。</param>
    private void PublishMonitorGlb(string? glbPath, string scannerId)
    {
        if (string.IsNullOrWhiteSpace(glbPath) || !File.Exists(glbPath))
        {
            return;
        }

        var machineArea = "printing";
        if (!string.IsNullOrWhiteSpace(scannerId)
            && _registry.TryGetEndpoint(scannerId, out var endpoint)
            && !string.IsNullOrWhiteSpace(endpoint.MachineArea))
        {
            machineArea = endpoint.MachineArea;
        }

        var pointer = ScanMonitorPaths.GlbPointerFile(_storage.ContentsRoot, machineArea);
        if (string.IsNullOrWhiteSpace(pointer))
        {
            return;
        }

        var dir = Path.GetDirectoryName(pointer);
        if (string.IsNullOrWhiteSpace(dir))
        {
            return;
        }

        Directory.CreateDirectory(dir);
        var tmp = pointer + ".tmp";
        File.WriteAllText(tmp, glbPath);
        File.Copy(tmp, pointer, overwrite: true);
        File.Delete(tmp);
    }

    /// <summary>
    /// 組面掃描失敗回應並寫入呼叫紀錄（沒有點雲／ZIP 時雜湊為空）。
    /// </summary>
    /// <param name="request">原始面掃描請求。</param>
    /// <param name="traceId">本次 traceId。</param>
    /// <param name="errorCode">回給台船的錯誤碼。</param>
    /// <param name="message">回給台船的訊息。</param>
    /// <param name="ct">取消權杖。</param>
    /// <param name="taskId">已建立或既有任務識別碼。</param>
    /// <param name="pointCloudPath">若 Pipeline 已產出點雲則一併記雜湊。</param>
    /// <param name="zipPath">若 Pipeline 已產出 ZIP 則一併記雜湊。</param>
    /// <param name="logAction"><c>ScanCallLog</c> 的 Action。</param>
    /// <returns>失敗的 API 包裝。</returns>
    private async Task<ApiResponse<SurfaceScanData>> SurfaceFailAsync(
        SurfaceScanRequest request,
        string traceId,
        string errorCode,
        string message,
        CancellationToken ct,
        string? taskId = null,
        string? pointCloudPath = null,
        string? zipPath = null,
        string? logAction = null)
    {
        var fail = ApiResponse<SurfaceScanData>.Fail(errorCode, message);
        await TryAppendScanLogAsync(
            BuildSurfaceLog(
                request,
                traceId,
                fail,
                taskId,
                seq: null,
                pointCloudPath,
                zipPath,
                logAction),
            ct).ConfigureAwait(false);
        return fail;
    }

    /// <summary>
    /// 組面掃描呼叫紀錄：request／response JSON，以及可選的 PLY／ZIP SHA256。
    /// </summary>
    /// <param name="request">面掃描請求。</param>
    /// <param name="traceId">本次 traceId。</param>
    /// <param name="response">即將回傳的 envelope。</param>
    /// <param name="taskId">任務識別碼。</param>
    /// <param name="seq">成功時的呼叫次數。</param>
    /// <param name="pointCloudPath">點雲絕對路徑。</param>
    /// <param name="zipPath">ZIP 絕對路徑。</param>
    /// <param name="logAction"><c>ScanCallLog</c> 的 Action；空白則記 <c>ScanSurface</c>。</param>
    /// <returns>待寫入的呼叫紀錄。</returns>
    private static ScanCallLogRecord BuildSurfaceLog(
        SurfaceScanRequest request,
        string traceId,
        ApiResponse<SurfaceScanData> response,
        string? taskId,
        int? seq,
        string? pointCloudPath,
        string? zipPath,
        string? logAction = null)
    {
        var action = logAction;
        if (string.IsNullOrWhiteSpace(action))
        {
            action = ScanCallLogActions.ScanSurface;
        }

        return new ScanCallLogRecord
        {
            Timestamp = DateTimeOffset.Now,
            TraceId = traceId,
            Action = action,
            ScannerId = request.ScannerId,
            JobId = request.JobId,
            TaskId = taskId,
            Seq = seq,
            Success = response.Success,
            ErrorCode = response.ErrorCode,
            ErrorMessage = response.ErrorMessage,
            RequestJson = Serialize(request),
            ResponseJson = Serialize(response),
            PointCloudPath = pointCloudPath,
            PointCloudSha256 = TryComputeSha256(pointCloudPath),
            ZipPath = zipPath,
            ZipSha256 = TryComputeSha256(zipPath),
        };
    }

    /// <summary>
    /// 組附件下載呼叫紀錄。成功時 <paramref name="zipPath"/> 會算 SHA256。
    /// </summary>
    /// <param name="traceId">本次 traceId。</param>
    /// <param name="taskId">任務識別碼。</param>
    /// <param name="requestJson">僅含 taskId 的 JSON。</param>
    /// <param name="response">即將回傳的結果（Controller 成功時改送檔案）。</param>
    /// <param name="zipPath">ZIP 絕對路徑。</param>
    /// <returns>待寫入的呼叫紀錄。</returns>
    private static ScanCallLogRecord BuildAttachmentLog(
        string traceId,
        string? taskId,
        string? requestJson,
        ApiResponse<SurfaceScanAttachmentFile> response,
        string? zipPath)
    {
        object responseBody = response.Success && response.Data is not null
            ? new { response.Data.DownloadFileName, zipPath }
            : response;
        return new ScanCallLogRecord
        {
            Timestamp = DateTimeOffset.Now,
            TraceId = traceId,
            Action = ScanCallLogActions.SurfaceAttachments,
            TaskId = taskId,
            Success = response.Success,
            ErrorCode = response.ErrorCode,
            ErrorMessage = response.ErrorMessage,
            RequestJson = requestJson,
            ResponseJson = Serialize(responseBody),
            ZipPath = zipPath,
            ZipSha256 = TryComputeSha256(zipPath),
        };
    }

    /// <summary>
    /// 寫入 <c>ScanCallLog</c>。資料庫失敗只記警告，不改變已組好的 API 回應。
    /// </summary>
    /// <param name="record">呼叫紀錄。</param>
    /// <param name="ct">取消權杖。</param>
    private async Task TryAppendScanLogAsync(ScanCallLogRecord record, CancellationToken ct)
    {
        try
        {
            await _scanCallLog.AppendAsync(record, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "寫入 ScanCallLog 失敗（traceId={TraceId} action={Action}）。", record.TraceId, record.Action);
        }
    }

    /// <summary>
    /// 將物件序列化成 camelCase JSON，供呼叫紀錄保存。
    /// </summary>
    /// <param name="value">request、envelope 或附件摘要。</param>
    /// <returns>JSON 字串；序列化失敗則為 null。</returns>
    private static string? Serialize(object value)
    {
        try
        {
            return JsonSerializer.Serialize(value, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 計算檔案 SHA256（大寫 hex）。檔案不存在或無法讀取時回 null。
    /// </summary>
    /// <param name="path">本機絕對路徑。</param>
    /// <returns>64 字元 hex，或 null。</returns>
    private static string? TryComputeSha256(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(path);
            var hash = SHA256.HashData(stream);
            return Convert.ToHexString(hash);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 管線前緣三點轉 API DTO。
    /// </summary>
    /// <param name="src">管線結果；null 表示未算出。</param>
    /// <returns>補償後左下／頂點／右下；未算出時為 null。</returns>
    private static CornerTripletDto? ToCornerTriplet(HeadFeaturesRecord? src)
    {
        if (src is null)
        {
            return null;
        }

        return new CornerTripletDto
        {
            LeftCorner = new CoordinateDto
            {
                X = src.LeftX,
                Y = src.LeftY,
                Z = src.LeftZ,
            },
            Apex = new CoordinateDto
            {
                X = src.ApexX,
                Y = src.ApexY,
                Z = src.ApexZ,
            },
            RightCorner = new CoordinateDto
            {
                X = src.RightX,
                Y = src.RightY,
                Z = src.RightZ,
            },
        };
    }
}
