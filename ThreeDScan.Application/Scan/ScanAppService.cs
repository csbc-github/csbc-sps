using ThreeDScan.Application.Calibration.Models;
using ThreeDScan.Application.Common;
using ThreeDScan.Application.Scan.Models;
using ThreeDScan.Domain.Abstractions;
using ThreeDScan.Domain.Models;

namespace ThreeDScan.Application.Scan;

/// <summary>掃描應用服務：面掃描走 C# Pipeline.Run；線／孔尚未實作。</summary>
public sealed class ScanAppService : IScanAppService
{
    private readonly IScannerRegistry _registry;
    private readonly IScannerCaptureFactory _captureFactory;
    private readonly ISurfaceScanPipeline _pipeline;
    private readonly ISurfaceScanTaskService _taskService;
    private readonly IStorageSettings _storage;

    public ScanAppService(
        IScannerRegistry registry,
        IScannerCaptureFactory captureFactory,
        ISurfaceScanPipeline pipeline,
        ISurfaceScanTaskService taskService,
        IStorageSettings storage)
    {
        _registry = registry;
        _captureFactory = captureFactory;
        _pipeline = pipeline;
        _taskService = taskService;
        _storage = storage;
    }

    /// <inheritdoc />
    public Task<ApiResponse<LineScanData>> ScanLineAsync(LineScanRequest request, CancellationToken ct = default)
    {
        _ = request;
        _ = ct;
        return Task.FromResult(SpecNotImplemented.Response<LineScanData>("POST /api/scan/line"));
    }

    /// <inheritdoc />
    public async Task<ApiResponse<SurfaceScanData>> ScanSurfaceAsync(
        SurfaceScanRequest request,
        CancellationToken ct = default)
    {
        var traceId = Guid.NewGuid().ToString("N");

        if (request.MeasureThickness)
        {
            return ApiResponse<SurfaceScanData>.Fail("SCAN_THK_001", "厚度量測尚未實作。");
        }

        if (!_registry.TryGetEndpoint(request.ScannerId, out var endpoint))
        {
            return ApiResponse<SurfaceScanData>.Fail("SCAN_CFG_001", $"未知 ScannerId: {request.ScannerId}");
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
                return ApiResponse<SurfaceScanData>.Fail("SCAN_001", "找不到對應的面掃描任務。");
            }

            if (!string.Equals(existing.ScannerId, request.ScannerId, StringComparison.OrdinalIgnoreCase))
            {
                return ApiResponse<SurfaceScanData>.Fail("SCAN_001", "scannerId 與既有任務不符。");
            }

            if (existing.IsCompleted)
            {
                return ApiResponse<SurfaceScanData>.Fail("SCAN_001", "任務已結束，請勿再呼叫。");
            }

            task = existing;
        }

        try
        {
            var programNo = _registry.ResolveProgramNo(request.ScannerId, endpoint.NavigatorScanNo);
            var capture = await _captureFactory.GetCapture(request.ScannerId).CaptureAsync(programNo, ct);
            var processed = await _pipeline.ProcessCaptureAsync(capture, ct);

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
                Thickness = null,
                SegmentZDelta = zDelta,
                TraceId = traceId,
            };
            return ApiResponse<SurfaceScanData>.Ok(data);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ApiResponse<SurfaceScanData>.Fail("SCAN_SDK_001", ex.Message);
        }
    }

    /// <inheritdoc />
    public Task<ApiResponse<SurfaceScanAttachmentFile>> GetSurfaceAttachmentsAsync(
        string taskId,
        CancellationToken ct = default)
    {
        _ = ct;
        if (string.IsNullOrWhiteSpace(taskId))
        {
            return Task.FromResult(
                ApiResponse<SurfaceScanAttachmentFile>.Fail("SCAN_001", "taskId 必填。"));
        }

        if (!_taskService.TryGet(taskId.Trim(), out var task) || task is null)
        {
            return Task.FromResult(
                ApiResponse<SurfaceScanAttachmentFile>.Fail("SCAN_001", "找不到對應的面掃描任務。"));
        }

        if (string.IsNullOrWhiteSpace(task.ZipPath) || !File.Exists(task.ZipPath))
        {
            return Task.FromResult(
                ApiResponse<SurfaceScanAttachmentFile>.Fail("SCAN_001", "此任務尚無附加檔。"));
        }

        var file = new SurfaceScanAttachmentFile
        {
            ZipFullPath = task.ZipPath,
            DownloadFileName = Path.GetFileName(task.ZipPath),
        };
        return Task.FromResult(ApiResponse<SurfaceScanAttachmentFile>.Ok(file));
    }

    /// <inheritdoc />
    public Task<ApiResponse<HoleScanData>> ScanHoleAsync(HoleScanRequest request, CancellationToken ct = default)
    {
        _ = request;
        _ = ct;
        return Task.FromResult(SpecNotImplemented.Response<HoleScanData>("POST /api/scan/hole"));
    }

    private string ToScanFileUrl(string fullPath)
    {
        var relative = Path.GetRelativePath(_storage.ExportRoot, fullPath);
        if (string.IsNullOrWhiteSpace(relative) || relative.StartsWith("..", StringComparison.Ordinal))
        {
            return fullPath;
        }

        return "/scan-files/" + relative.Replace('\\', '/');
    }
}
