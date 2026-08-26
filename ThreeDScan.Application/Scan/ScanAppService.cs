using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ThreeDScan.Application.Calibration.Models;
using ThreeDScan.Application.Common;
using ThreeDScan.Application.Scan.Models;
using ThreeDScan.Domain.Abstractions;
using ThreeDScan.Domain.Models;
using ThreeDScan.Domain.Repositories;

namespace ThreeDScan.Application.Scan;

/// <summary>掃描應用服務：面掃描走 C# Pipeline.Run；線／孔尚未實作。</summary>
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
    /// <param name="logger">寫入呼叫紀錄失敗時記警告，掃描 API 仍回已組好的回應。</param>
    public ScanAppService(
        IScannerRegistry registry,
        IScannerCaptureFactory captureFactory,
        ISurfaceScanPipeline pipeline,
        ISurfaceScanTaskService taskService,
        IStorageSettings storage,
        IJobThicknessRepository jobThickness,
        IScanCallLogRepository scanCallLog,
        ILogger<ScanAppService> logger)
    {
        _registry = registry;
        _captureFactory = captureFactory;
        _pipeline = pipeline;
        _taskService = taskService;
        _storage = storage;
        _jobThickness = jobThickness;
        _scanCallLog = scanCallLog;
        _logger = logger;
    }

    /// <inheritdoc />
    /// <remarks>線掃描尚未實作，固定回 501 <c>SYS_NYI</c>，仍寫入 <c>ScanCallLog</c>。</remarks>
    public async Task<ApiResponse<LineScanData>> ScanLineAsync(
        LineScanRequest request,
        CancellationToken ct = default)
    {
        var response = SpecNotImplemented.Response<LineScanData>("POST /api/scan/line");
        await TryAppendScanLogAsync(
            new ScanCallLogRecord
            {
                Timestamp = DateTimeOffset.Now,
                TraceId = Guid.NewGuid().ToString("N"),
                Action = ScanCallLogActions.ScanLine,
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
    /// <remarks>
    /// <c>measureThickness=true</c>：量兩翼厚度，寫入 <c>JobThickness</c>，回應帶 thicknessA／B。
    /// <c>measureThickness=false</c>：依 jobId 讀 <c>JobThickness</c> 組模型。
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
            var programNo = _registry.ResolveProgramNo(request.ScannerId, endpoint.NavigatorScanNo);
            ThicknessMeasureRequest? thickness = null;
            JobThicknessRecord? storedThickness = null;
            if (request.MeasureThickness)
            {
                if (request.ThicknessPointA is null || request.ThicknessPointB is null)
                {
                    return await SurfaceFailAsync(
                        request,
                        traceId,
                        "SCAN_THK_001",
                        "measureThickness=true 時 thicknessPointA / thicknessPointB 必填。",
                        ct,
                        task.TaskId)
                        .ConfigureAwait(false);
                }

                thickness = new ThicknessMeasureRequest
                {
                    Ax = request.ThicknessPointA.X,
                    Ay = request.ThicknessPointA.Y,
                    Az = request.ThicknessPointA.Z,
                    Bx = request.ThicknessPointB.X,
                    By = request.ThicknessPointB.Y,
                    Bz = request.ThicknessPointB.Z,
                };
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

            var capture = await _captureFactory.GetCapture(request.ScannerId)
                .CaptureAsync(programNo, ct)
                .ConfigureAwait(false);
            var processed = await _pipeline.ProcessCaptureAsync(capture, ct, thickness, storedThickness)
                .ConfigureAwait(false);
            if (request.MeasureThickness
                && (processed.ThicknessA is null || processed.ThicknessB is null))
            {
                return await SurfaceFailAsync(
                    request,
                    traceId,
                    "SCAN_THK_001",
                    "厚度量測失敗：點雲沒有可用點。",
                    ct,
                    task.TaskId,
                    processed.PointCloudPath,
                    processed.ZipPath)
                    .ConfigureAwait(false);
            }

            if (request.MeasureThickness
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
            if (task.LastMeanZ.HasValue)
            {
                zDelta = processed.MeanZ - task.LastMeanZ.Value;
            }

            task.Seq = seq;
            task.ZipPath = processed.ZipPath;
            task.PointCloudPath = processed.PointCloudPath;
            task.LastMeanZ = processed.MeanZ;
            task.IsCompleted = request.Final;
            task.UpdatedAt = DateTimeOffset.Now;
            _taskService.Save(task);

            var data = new SurfaceScanData
            {
                TaskId = task.TaskId,
                Seq = seq,
                Status = request.Final ? SurfaceScanData.StatusFinal : SurfaceScanData.StatusContinue,
                PointCloudUrl = ToScanFileUrl(processed.PointCloudPath),
                ThicknessA = request.MeasureThickness ? processed.ThicknessA : null,
                ThicknessB = request.MeasureThickness ? processed.ThicknessB : null,
                SegmentZDelta = zDelta,
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
                    processed.PointCloudPath,
                    processed.ZipPath),
                ct).ConfigureAwait(false);
            return ok;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var code = "SCAN_SDK_001";
            var message = ex.Message;
            if (request.MeasureThickness
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
    /// <returns>失敗的 API 包裝。</returns>
    private async Task<ApiResponse<SurfaceScanData>> SurfaceFailAsync(
        SurfaceScanRequest request,
        string traceId,
        string errorCode,
        string message,
        CancellationToken ct,
        string? taskId = null,
        string? pointCloudPath = null,
        string? zipPath = null)
    {
        var fail = ApiResponse<SurfaceScanData>.Fail(errorCode, message);
        await TryAppendScanLogAsync(
            BuildSurfaceLog(request, traceId, fail, taskId, seq: null, pointCloudPath, zipPath),
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
    /// <returns>待寫入的呼叫紀錄。</returns>
    private static ScanCallLogRecord BuildSurfaceLog(
        SurfaceScanRequest request,
        string traceId,
        ApiResponse<SurfaceScanData> response,
        string? taskId,
        int? seq,
        string? pointCloudPath,
        string? zipPath)
    {
        return new ScanCallLogRecord
        {
            Timestamp = DateTimeOffset.Now,
            TraceId = traceId,
            Action = ScanCallLogActions.ScanSurface,
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
}
