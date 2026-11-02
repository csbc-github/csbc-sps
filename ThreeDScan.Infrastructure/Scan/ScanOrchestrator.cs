using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using ThreeDScan.Application.Common;
using ThreeDScan.Application.Common.Models;
using ThreeDScan.Application.Scan;
using ThreeDScan.Application.Scan.Models;
using ThreeDScan.Infrastructure.Configuration;
using ThreeDScan.Domain.Abstractions;
using ThreeDScan.Domain.Models;
using ThreeDScan.Domain.Repositories;
using ThreeDScan.Infrastructure.Export;
using ThreeDScan.Infrastructure.Export.Models;
using FeatureOpeningModel = ThreeDScan.Infrastructure.Export.Models.FeatureOpening;
using ThreeDScan.Infrastructure.Scanners;

namespace ThreeDScan.Infrastructure.Scan;

/// <summary>面掃描／孔洞正式路徑。</summary>
/// <remarks>由 Hank Scan3D 的 ScanOrchestrator Surface／Hole 搬入，邏輯不改；補償每段重讀 CompensationHistory。</remarks>
public sealed class ScanOrchestrator : IScanOrchestrator
{
    private readonly IScanTaskRepository _store;
    private readonly IScannerRegistry _registry;
    private readonly IScannerCaptureFactory _capture;
    private readonly ICompensationRepository _compensation;
    private readonly IJobThicknessRepository _jobThickness;
    private readonly IDeviceCoordinator _devices;
    private readonly IScanWorkQueue _queue;
    private readonly IStorageSettings _storage;
    private readonly IConfiguration _configuration;
    private readonly SurfaceScanOrchestratorOptions _surfaceScanFormal;
    private readonly CallbackSettings _callbackSettings;
    private readonly ISystemRuntimeConfig _runtimeConfig;

    /// <summary>建立協調器。</summary>
    public ScanOrchestrator(
        IScanTaskRepository store,
        IScannerRegistry registry,
        IScannerCaptureFactory capture,
        ICompensationRepository compensation,
        IJobThicknessRepository jobThickness,
        IDeviceCoordinator devices,
        IScanWorkQueue queue,
        IStorageSettings storage,
        IConfiguration configuration,
        IOptions<SurfaceScanOrchestratorOptions> surfaceScanFormal,
        IOptions<CallbackSettings> callbackSettings,
        ISystemRuntimeConfig runtimeConfig)
    {
        _store = store;
        _registry = registry;
        _capture = capture;
        _compensation = compensation;
        _jobThickness = jobThickness;
        _devices = devices;
        _queue = queue;
        _storage = storage;
        _configuration = configuration;
        _surfaceScanFormal = surfaceScanFormal.Value ?? new SurfaceScanOrchestratorOptions();
        _callbackSettings = callbackSettings.Value ?? new CallbackSettings();
        _runtimeConfig = runtimeConfig;
    }

    /// <inheritdoc />
    public async Task<ScanOutcome> StartSurfaceAsync(
        string scannerId,
        string jobId,
        Pose5 cameraPosition,
        int? navigatorScanNo,
        Point3? innerLeft,
        Point3? innerRight,
        double? materialThicknessMm,
        ScannerRoi? roi,
        int? componentRank,
        StitchMode stitchMode,
        double nominalGapMm,
        string traceId,
        string baseUrl,
        CancellationToken ct)
    {
        Required(jobId, "jobId");
        Validate(cameraPosition);
        ValidateNavigator(navigatorScanNo);
        ValidateRoi(roi);
        if (componentRank is <= 0)
        {
            throw new ScanTaskException(400, "INVALID_REQUEST", "componentRank 必須大於 0。");
        }

        if (!Enum.IsDefined(stitchMode))
        {
            throw new ScanTaskException(400, "INVALID_STITCH_MODE", "stitchMode 僅支援 CameraPose 或 NominalButtJoin。");
        }

        if (innerLeft is not null)
        {
            Validate(innerLeft, "innerLeft");
        }

        if (innerRight is not null)
        {
            Validate(innerRight, "innerRight");
        }

        if (materialThicknessMm is { } t && (!double.IsFinite(t) || t <= 0 || t > 1000))
        {
            throw new ScanTaskException(400, "INVALID_THICKNESS", "materialThicknessMm 需介於 0～1000 mm（不含 0）。");
        }

        if (!double.IsFinite(nominalGapMm) || nominalGapMm < 0)
        {
            throw new ScanTaskException(400, "INVALID_GAP", "nominalGapMm 必須是非負有限數值。");
        }

        var stored = await _jobThickness.GetByJobIdAsync(jobId, ct).ConfigureAwait(false);
        var scannerDefaultThickness = _registry.GetDefaultMaterialThicknessMm(scannerId);
        if (innerLeft is null
            && innerRight is null
            && materialThicknessMm is null
            && stored is null
            && scannerDefaultThickness is null)
        {
            throw new ScanTaskException(400, "SCAN_THK_002", "未提供 inner 座標，且找不到此工單的既有厚度。");
        }

        var d = await NewTask("Surface", scannerId, jobId, traceId, baseUrl, ct).ConfigureAwait(false);
        try
        {
            await _store.UpdateAsync(d.TaskId, x =>
            {
                x.Phase = "Capturing";
                x.MaterialThicknessMm = materialThicknessMm ?? scannerDefaultThickness;
                x.StitchMode = stitchMode;
                x.NominalGapMm = nominalGapMm;
            }, ct).ConfigureAwait(false);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _queue.Token(d.TaskId));
            var fingerprintSource = new SurfaceStartFingerprintRequest
            {
                ScannerId = scannerId,
                JobId = jobId,
                CameraPosition = cameraPosition,
                NavigatorScanNo = navigatorScanNo,
                InnerLeft = innerLeft,
                InnerRight = innerRight,
                MaterialThicknessMm = materialThicknessMm,
                Roi = roi,
                ComponentRank = componentRank,
                StitchMode = stitchMode,
                NominalGapMm = nominalGapMm,
            };
            var fingerprint = Fingerprint(fingerprintSource);
            var outcome = await CaptureSegmentAsync(
                d,
                cameraPosition,
                navigatorScanNo,
                innerLeft,
                innerRight,
                roi,
                componentRank,
                null,
                fingerprint,
                first: true,
                stored,
                linked.Token,
                traceId).ConfigureAwait(false);
            WriteStatus("start", outcome.Document, 1);
            return outcome;
        }
        catch (OperationCanceledException)
        {
            await MarkInterruptedAsync(d.TaskId, false).ConfigureAwait(false);
            ReleaseDevice(d.TaskId);
            throw;
        }
        catch (Exception ex)
        {
            await MarkFailedAsync(d.TaskId, ex).ConfigureAwait(false);
            ReleaseDevice(d.TaskId);
            WriteStatus("failed", d, null, ex.Message);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<ScanOutcome> SegmentSurfaceAsync(
        string taskId,
        Pose5 cameraPosition,
        int? navigatorScanNo,
        ScannerRoi? roi,
        int? componentRank,
        string? segmentKey,
        string traceId,
        CancellationToken ct)
    {
        Validate(cameraPosition);
        ValidateNavigator(navigatorScanNo);
        ValidateRoi(roi);
        if (componentRank is <= 0)
        {
            throw new ScanTaskException(400, "INVALID_REQUEST", "componentRank 必須大於 0。");
        }

        var d = await _store.GetAsync(taskId, "Surface", ct).ConfigureAwait(false);
        var fingerprintSource = new SurfaceSegmentFingerprintRequest
        {
            TaskId = taskId,
            CameraPosition = cameraPosition,
            NavigatorScanNo = navigatorScanNo,
            SegmentKey = segmentKey,
            Roi = roi,
            ComponentRank = componentRank,
        };
        var fingerprint = Fingerprint(fingerprintSource);
        var existing = segmentKey is null ? null : d.Segments.FirstOrDefault(x => x.SegmentKey == segmentKey);
        if (existing is not null)
        {
            if (existing.RequestFingerprint != fingerprint)
            {
                throw new ScanTaskException(409, "IDEMPOTENCY_CONFLICT", "segmentKey 已使用於不同請求。");
            }

            if (d.Status == "Aborted")
            {
                throw new ScanTaskException(409, "TASK_ABORTED", "任務已取消。");
            }

            return new ScanOutcome { Document = d, Response = existing.Response };
        }

        d = await _store.UpdateAsync(taskId, x =>
        {
            ExpectPhase(x, "Collecting");
            x.Phase = "Capturing";
        }, ct).ConfigureAwait(false);
        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _queue.Token(d.TaskId));
            var outcome = await CaptureSegmentAsync(
                d,
                cameraPosition,
                navigatorScanNo,
                null,
                null,
                roi,
                componentRank,
                segmentKey,
                fingerprint,
                first: false,
                null,
                linked.Token,
                traceId).ConfigureAwait(false);
            WriteStatus("segment", outcome.Document, outcome.Document.Segments.Count);
            return outcome;
        }
        catch (OperationCanceledException)
        {
            await MarkInterruptedAsync(d.TaskId, false).ConfigureAwait(false);
            ReleaseDevice(d.TaskId);
            throw;
        }
        catch (Exception ex)
        {
            await MarkFailedAsync(d.TaskId, ex).ConfigureAwait(false);
            ReleaseDevice(d.TaskId);
            WriteStatus("failed", d, null, ex.Message);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<ScanOutcome> CompleteSurfaceAsync(
        string taskId,
        string callbackUrl,
        string traceId,
        CancellationToken ct)
    {
        ValidateCallback(callbackUrl);
        var d = await _store.GetAsync(taskId, "Surface", ct).ConfigureAwait(false);
        if (d.Phase == "Stitching" || d.Status == "Completed")
        {
            return new ScanOutcome
            {
                Document = d,
                Response = JsonDefaults.Element(new { d.TaskId, d.Status }),
            };
        }

        await _store.UpdateAsync(taskId, x =>
        {
            ExpectPhase(x, "Collecting");
            if (x.Segments.Count == 0)
            {
                throw new ScanTaskException(409, "NO_SEGMENTS", "尚無掃描段落。");
            }

            x.Phase = "Stitching";
            x.CallbackUrl = callbackUrl;
            if (!string.IsNullOrWhiteSpace(traceId))
            {
                x.TraceId = traceId;
            }
        }, ct).ConfigureAwait(false);
        try
        {
            _queue.Enqueue(new ScanWorkItem { TaskId = d.TaskId, Operation = "SurfaceComplete" });
        }
        catch (Exception ex)
        {
            await MarkFailedAsync(d.TaskId, ex).ConfigureAwait(false);
            ReleaseDevice(d.TaskId);
            throw;
        }

        WriteStatus("complete", d, d.Segments.Count);
        return new ScanOutcome
        {
            Document = d,
            Response = JsonDefaults.Element(new { d.TaskId, status = "Processing" }),
        };
    }

    /// <inheritdoc />
    public async Task ExecuteSurfaceAsync(string taskId, CancellationToken ct)
    {
        var d = await _store.GetAsync(taskId, "Surface", ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        if (d.Terminal)
        {
            return;
        }

        var segments = new List<SegmentProcessingResult>();
        foreach (var segment in d.Segments)
        {
            var json = await File.ReadAllTextAsync(segment.ProcessingResultPath, ct).ConfigureAwait(false);
            segments.Add(JsonSerializer.Deserialize<SegmentProcessingResult>(json, JsonDefaults.Options)!);
        }

        var output = Path.Combine(_store.TaskDirectory(d.TaskId), "complete");
        var meshCell = _registry.GetMeshCellMm(d.ScannerId);
        var result = await Task.Run(
            () => StitchProcessor.Stitch(
                segments,
                output,
                d.MaterialThicknessMm,
                new StitchProcessingOptions
                {
                    Mode = d.StitchMode,
                    NominalGapMm = d.NominalGapMm,
                    MeshCellMm = meshCell,
                },
                ct),
            ct).ConfigureAwait(false);
        if (!result.ValidationPassed)
        {
            throw new ScanTaskException(422, "STITCH_QUALITY_FAILED", "拼接檢查未通過。", new { result.Warnings });
        }

        var zip = await BuildAttachmentsAsync(d, result, ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        await _store.UpdateAsync(d.TaskId, x =>
        {
            ExpectPhase(x, "Stitching");
            x.Artifacts["merged"] = new ArtifactDocument
            {
                Path = result.StitchedPlyPath,
                ContentType = "application/octet-stream",
                DownloadName = $"{d.TaskId}-merged.ply",
            };
            x.Artifacts["stitch-report"] = new ArtifactDocument
            {
                Path = result.ReportPath,
                ContentType = "application/json",
                DownloadName = "stitch-report.json",
            };
            if (zip is not null)
            {
                x.Artifacts["attachments"] = new ArtifactDocument
                {
                    Path = zip,
                    ContentType = "application/zip",
                    DownloadName = $"{d.TaskId}-attachments.zip",
                };
                x.AttachmentsArtifactId = "attachments";
            }
            else
            {
                x.AttachmentsUnavailableReason = d.MaterialThicknessMm is null
                    ? "ATTACHMENTS_THICKNESS_REQUIRED"
                    : "ATTACHMENTS_MODEL_UNAVAILABLE";
            }

            x.Status = "Completed";
            x.Phase = "Completed";
            x.Result = JsonDefaults.Element(new
            {
                pointCloudUrl = ArtifactUrl(x, "merged"),
                attachmentsAvailable = zip is not null,
                attachmentsUrl = zip is null ? null : x.BaseUrl + $"/api/scan/surface/{d.TaskId}/attachments",
                result.SegmentCount,
                result.PointCount,
                result.AbsoluteCoordinatesCalibrated,
                result.ThicknessMm,
                result.Warnings,
                isReplay = x.IsReplay,
                coordinateFrame = "ConfiguredAbsolute",
                x.AttachmentsUnavailableReason,
            });
        }, ct).ConfigureAwait(false);
        WriteGlbPointer(d, result.GlbPath);
        WriteStatus("complete", d, d.Segments.Count, "拼接完成");
    }

    /// <inheritdoc />
    public async Task<ScanOutcome> StartHoleAsync(
        string scannerId,
        string jobId,
        string callbackUrl,
        string? holeType,
        Pose5? cameraPosition,
        ScannerRoi? roi,
        string? featureId,
        int? navigatorScanNo,
        string traceId,
        string baseUrl,
        CancellationToken ct)
    {
        Required(jobId, "jobId");
        ValidateCallback(callbackUrl);
        ValidateNavigator(navigatorScanNo);
        ValidateRoi(roi);
        if (cameraPosition is not null)
        {
            Validate(cameraPosition);
        }

        if (holeType is not (null or "Circle" or "Slot" or "EdgeNotch"))
        {
            throw new ScanTaskException(400, "INVALID_HOLE_TYPE", "holeType 需為 Circle、Slot 或 EdgeNotch。");
        }

        if (holeType == "EdgeNotch")
        {
            throw new ScanTaskException(501, "EDGE_NOTCH_UNVERIFIED", "目前設備孔緣核心尚未驗證開放缺口；不會將掃描截斷誤當 EdgeNotch。");
        }

        var d = await NewTask("Hole", scannerId, jobId, traceId, baseUrl, ct).ConfigureAwait(false);
        try
        {
            await _store.UpdateAsync(d.TaskId, x =>
            {
                x.Phase = "HoleCapture";
                x.CallbackUrl = callbackUrl;
                x.WorkRequest = JsonDefaults.Element(new
                {
                    scannerId,
                    jobId,
                    callbackUrl,
                    holeType,
                    cameraPosition,
                    roi,
                    featureId,
                    navigatorScanNo,
                });
            }, ct).ConfigureAwait(false);
            _queue.Enqueue(new ScanWorkItem { TaskId = d.TaskId, Operation = "Hole" });
        }
        catch (Exception ex)
        {
            await MarkFailedAsync(d.TaskId, ex).ConfigureAwait(false);
            ReleaseDevice(d.TaskId);
            throw;
        }

        return new ScanOutcome
        {
            Document = d,
            Response = JsonDefaults.Element(new { d.TaskId, status = "Processing" }),
        };
    }

    /// <inheritdoc />
    public async Task ExecuteHoleAsync(string taskId, CancellationToken ct)
    {
        var d = await _store.GetAsync(taskId, "Hole", ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        if (d.Terminal || d.WorkRequest is null)
        {
            return;
        }

        var req = d.WorkRequest.Value;
        var holeType = req.TryGetProperty("holeType", out var ht) ? ht.GetString() : null;
        var featureId = req.TryGetProperty("featureId", out var fid) ? fid.GetString() : null;
        int? navigator = req.TryGetProperty("navigatorScanNo", out var nav) && nav.ValueKind == JsonValueKind.Number
            ? nav.GetInt32()
            : null;
        Pose5? requestPose = null;
        if (req.TryGetProperty("cameraPosition", out var poseEl) && poseEl.ValueKind == JsonValueKind.Object)
        {
            requestPose = poseEl.Deserialize<Pose5>(JsonDefaults.Options);
        }

        ScannerRoi? requestRoi = null;
        if (req.TryGetProperty("roi", out var roiEl) && roiEl.ValueKind == JsonValueKind.Object)
        {
            requestRoi = roiEl.Deserialize<ScannerRoi>(JsonDefaults.Options);
        }

        var effectiveRoi = requestRoi;
        if (effectiveRoi is null && _registry.TryGetHoleRoi(d.ScannerId, out var holeRoi))
        {
            effectiveRoi = holeRoi;
        }

        ValidateRoi(effectiveRoi);

        var captureDir = Path.Combine(_store.TaskDirectory(d.TaskId), "capture");
        var holeCapture = await CaptureRawPlyAsync(d, navigator, captureDir, ct).ConfigureAwait(false);
        var rawPly = holeCapture.RawPlyPath;
        var capturedAt = DateTimeOffset.UtcNow;
        var analysis = await Task.Run(
            () => HoleAnalysis.AnalyzeFile(rawPly, holeType, ToFeatureRoi(effectiveRoi), ct),
            ct).ConfigureAwait(false);

        FeatureOpeningModel? selected;
        if (featureId is null)
        {
            selected = analysis.Features.FirstOrDefault(f => f.Id == analysis.SelectedFeatureId);
        }
        else
        {
            selected = analysis.Features.FirstOrDefault(f => f.Id == featureId);
        }

        if (featureId is not null && selected is null)
        {
            throw new ScanTaskException(
                400,
                "FEATURE_NOT_FOUND",
                "找不到指定 featureId。",
                new { available = analysis.Features.Select(f => f.Id).ToArray() });
        }

        if (holeType is not null
            && !analysis.Features.Any(f => f.Type == holeType && f.Closed))
        {
            throw new ScanTaskException(
                400,
                "HOLE_TYPE_NOT_FOUND",
                "沒有通過指定孔型品質檢查的完整孔。",
                new { analysis.Features, analysis.Warnings });
        }

        if (holeType is not null
            && selected is not null
            && (!selected.Closed || selected.Type != holeType))
        {
            throw new ScanTaskException(
                400,
                "FEATURE_TYPE_MISMATCH",
                "指定 featureId 不是通過指定孔型品質檢查的完整孔。");
        }

        var pose = requestPose;
        if (pose is null && _registry.TryGetFixedCameraPosition(d.ScannerId, out var fixedPose))
        {
            pose = fixedPose;
        }

        var coordinates = d.CoordinatesSnapshot;
        double[][]? matrix = null;
        if (pose is not null)
        {
            matrix = RigidTransforms.FromPose(pose, coordinates);
        }

        var warnings = analysis.Warnings.ToList();
        if (analysis.Features.Length == 0)
        {
            warnings.Add("未觀察到可解析開口，不代表工件實際無孔；遮擋與取樣密度可能造成漏測。");
        }

        if (selected is { Closed: false })
        {
            warnings.Add("明確選取的孔緣不完整，不能當成完整孔或 EdgeNotch 的認證結果。");
        }

        if (pose is null)
        {
            warnings.Add("沒有提供拍攝姿態；featureBoundary 使用掃描器原始座標，不是工廠絕對座標。");
        }
        else if (!coordinates.IsCalibrated)
        {
            warnings.Add("掃描器外參尚未驗證，絕對座標僅依目前設定換算。");
        }

        var features = MapHoleFeatures(analysis.Features, matrix);
        var featureBoundary = (selected?.Boundary ?? Array.Empty<FeaturePoint>())
            .Select(p => MapHolePoint(p, matrix))
            .ToArray();
        var boundarySelection = featureId is null ? analysis.BoundarySelection : "ExplicitFeatureId";
        var coordinateFrame = pose is null ? "Scanner" : "ConfiguredAbsolute";
        var absoluteCoordinatesCalibrated = pose is not null && coordinates.IsCalibrated;

        var resultPayload = new
        {
            scanId = "SCAN-" + d.TaskId,
            capturedAt,
            featureBoundary,
            selectedFeatureId = selected?.Id,
            features,
            boundarySelection,
            analysis.PointCount,
            analysis.ScanPitchXMm,
            analysis.ScanPitchYMm,
            coordinateFrame,
            absoluteCoordinatesCalibrated,
            warnings,
            isReplay = d.IsReplay,
        };

        var reportPath = Path.Combine(_store.TaskDirectory(d.TaskId), "hole-result.json");
        await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(resultPayload, JsonDefaults.Options), ct)
            .ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        await _store.UpdateAsync(d.TaskId, x =>
        {
            ExpectPhase(x, "HoleCapture");
            x.CapturedAt = capturedAt;
            x.Status = "Completed";
            x.Phase = "Completed";
            x.Artifacts["hole-result"] = new ArtifactDocument
            {
                Path = reportPath,
                ContentType = "application/json",
                DownloadName = "hole-result.json",
            };
            x.Artifacts["raw"] = new ArtifactDocument
            {
                Path = rawPly,
                ContentType = "application/octet-stream",
                DownloadName = "raw-scanner.ply",
            };
            x.Result = JsonDefaults.Element(resultPayload);
        }, ct).ConfigureAwait(false);
        WriteHoleBoundary(d, capturedAt, featureBoundary);
    }

    /// <inheritdoc />
    public async Task<ScanOutcome> AbortAsync(string taskId, string? kind, CancellationToken ct)
    {
        var d = await _store.GetAsync(taskId, kind, ct).ConfigureAwait(false);
        if (d.Status == "Aborted")
        {
            return new ScanOutcome
            {
                Document = d,
                Response = JsonDefaults.Element(new { d.TaskId, d.Status, d.TraceId }),
            };
        }

        if (d.Terminal)
        {
            throw new ScanTaskException(409, "TASK_ALREADY_FINISHED", "已完成或失敗的任務不能再取消。");
        }

        var deferRelease = false;
        _queue.Cancel(d.TaskId);
        d = await _store.UpdateAsync(d.TaskId, x =>
        {
            if (x.Terminal && x.Status != "Aborted")
            {
                throw new ScanTaskException(409, "TASK_ALREADY_FINISHED", "任務已完成。");
            }

            deferRelease = x.Phase == "Capturing";
            x.Status = "Aborted";
            x.Phase = "Aborted";
            x.Result = null;
            x.Artifacts.Clear();
        }, ct).ConfigureAwait(false);
        if (!deferRelease)
        {
            ReleaseDevice(d.TaskId);
        }

        WriteStatus("abort", d, null);
        return new ScanOutcome
        {
            Document = d,
            Response = JsonDefaults.Element(new { d.TaskId, d.Status, d.TraceId }),
        };
    }

    /// <inheritdoc />
    public Task<TaskDocument?> FindAsync(string taskId, CancellationToken ct)
    {
        return _store.FindAsync(taskId, ct);
    }

    /// <inheritdoc />
    public async Task<(string Path, string ContentType, string DownloadName)> GetArtifactAsync(
        string taskId,
        string artifactId,
        CancellationToken ct)
    {
        var task = await _store.GetAsync(taskId, ct: ct).ConfigureAwait(false);
        if (task.Status == "Aborted")
        {
            throw new ScanTaskException(410, "TASK_ABORTED", "此任務已取消，附件不可下載。");
        }

        if (!task.Artifacts.TryGetValue(artifactId, out var artifact))
        {
            throw new ScanTaskException(404, "ARTIFACT_NOT_FOUND", "找不到此任務的檔案。");
        }

        var path = Path.GetFullPath(artifact.Path);
        var root = _store.TaskDirectory(taskId) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new ScanTaskException(404, "ARTIFACT_NOT_FOUND", "檔案不屬於此任務。");
        }

        if (!File.Exists(path))
        {
            throw new ScanTaskException(410, "ARTIFACT_EXPIRED", "檔案已不存在。");
        }

        return (path, artifact.ContentType, artifact.DownloadName);
    }

    /// <inheritdoc />
    public async Task<(string Path, string ContentType, string DownloadName)> GetAttachmentsAsync(
        string taskId,
        CancellationToken ct)
    {
        var task = await _store.GetAsync(taskId, "Surface", ct).ConfigureAwait(false);
        if (task.Status != "Completed")
        {
            throw new ScanTaskException(409, "TASK_NOT_COMPLETED", "請等待面掃描任務 Completed 後下載。");
        }

        if (task.AttachmentsArtifactId is null)
        {
            throw new ScanTaskException(
                409,
                task.AttachmentsUnavailableReason ?? "ATTACHMENTS_UNAVAILABLE",
                "點雲已完成，但沒有可用的 STEP/glTF 模型；請確認板厚與模型重建條件。");
        }

        return await GetArtifactAsync(taskId, task.AttachmentsArtifactId, ct).ConfigureAwait(false);
    }

    /// <summary>背景失敗標記。</summary>
    /// <remarks>來自 Hank Scan3D 的 <c>Scan3D.Service.ScanOrchestrator</c>。</remarks>
    public Task MarkFailedAsync(string taskId, Exception error)
    {
        var code = error is ScanTaskException scan ? scan.Code : "SYS_INTERNAL";
        var message = error.Message;
        return _store.UpdateAsync(taskId, doc =>
        {
            if (doc.Terminal)
            {
                return;
            }

            doc.Status = "Failed";
            doc.Phase = "Failed";
            doc.Result = null;
            doc.Error = new TaskApiError { Code = code, Message = message };
        });
    }

    /// <summary>中斷標記。</summary>
    /// <remarks>來自 Hank Scan3D 的 <c>Scan3D.Service.ScanOrchestrator</c>。</remarks>
    public Task MarkInterruptedAsync(string taskId, bool shuttingDown)
    {
        return _store.UpdateAsync(taskId, doc =>
        {
            if (doc.Terminal)
            {
                return;
            }

            doc.Artifacts.Clear();
            doc.Result = null;
            if (shuttingDown)
            {
                doc.Status = "Failed";
                doc.Phase = "Failed";
                doc.Error = new TaskApiError
                {
                    Code = "SERVICE_STOPPED",
                    Message = "服務停止造成工作中斷；請重新發起掃描。",
                };
            }
            else
            {
                doc.Status = "Aborted";
                doc.Phase = "Aborted";
                doc.Error = null;
            }
        });
    }

    /// <summary>釋放掃描器。</summary>
    /// <remarks>來自 Hank Scan3D 的 <c>Scan3D.Service.ScanOrchestrator</c>。</remarks>
    public void ReleaseDevice(string taskId)
    {
        _devices.ReleaseByTaskId(taskId);
        _queue.Finished(taskId);
    }

    /// <summary>拍攝一段、抽取幾何並寫入任務。</summary>
    /// <remarks>來自 Hank Scan3D 的 <c>Scan3D.Service.ScanOrchestrator</c>。</remarks>
    private async Task<ScanOutcome> CaptureSegmentAsync(
        TaskDocument d,
        Pose5 pose,
        int? navigator,
        Point3? left,
        Point3? right,
        ScannerRoi? roi,
        int? rank,
        string? key,
        string fingerprint,
        bool first,
        JobThicknessRecord? storedThickness,
        CancellationToken ct,
        string? requestTraceId)
    {
        var number = d.Segments.Count + 1;
        var directory = Path.Combine(_store.TaskDirectory(d.TaskId), $"segment-{number:D3}");
        var capture = await CaptureRawPlyAsync(d, navigator, directory, ct).ConfigureAwait(false);
        var rawPly = capture.RawPlyPath;
        var capturedAt = DateTimeOffset.UtcNow;
        var coordinates = await CoordinatesForSegmentAsync(d.ScannerId, ct).ConfigureAwait(false);
        if (!_registry.TryGetProcessingDefaults(d.ScannerId, out var processingDefaults))
        {
            processingDefaults = new SegmentProcessingOptions();
        }

        var options = MergeSegmentProcessing(processingDefaults, roi, rank);
        var processed = await Task.Run(
            () => new SegmentProcessor(coordinates).ProcessSegment(
                rawPly,
                Path.Combine(directory, "processed"),
                pose,
                left,
                right,
                ct,
                options),
            ct).ConfigureAwait(false);
        if (processed.Geometry is null)
        {
            throw new ScanTaskException(
                422,
                "SURF_GEOMETRY_UNAVAILABLE",
                "未取得可用的兩翼角鋼量測。可調整 scanner ROI 或 componentRank。",
                new { d.TaskId, processed.GeometryError, processed.Candidates });
        }

        var resultPath = Path.Combine(directory, "segment-result.json");
        await SegmentFileAtomicWrite.WriteJsonAsync(resultPath, processed, ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        var artifactId = $"segment-{number:D3}-pointcloud";
        var g = processed.Geometry;
        var warnings = processed.Warnings.ToList();
        if (capture.IsReplay)
        {
            warnings.Add("REPLAY：本次資料來自既有點雲，沒有觸發硬體。");
        }
        var thicknessLeft = g.LeftThickness;
        var thicknessRight = g.RightThickness;
        if (first && left is null && right is null && storedThickness is not null)
        {
            thicknessLeft = storedThickness.ThicknessA;
            thicknessRight = storedThickness.ThicknessB;
        }

        var common = new Dictionary<string, object?>
        {
            ["pointCloudUrl"] = ArtifactUrl(d, artifactId),
            ["headFeatures"] = g.HeadFeatures,
            ["segmentZDelta"] = g.SegmentZDelta,
            ["traceId"] = requestTraceId ?? d.TraceId,
            ["coordinateFrame"] = "ConfiguredAbsolute",
            ["absoluteCoordinatesCalibrated"] = processed.AbsoluteCoordinatesCalibrated,
            ["calibrationId"] = processed.CalibrationId,
            ["segmentNo"] = number,
            ["capturedAt"] = capturedAt,
            ["warnings"] = warnings,
            ["isReplay"] = capture.IsReplay,
            ["capture"] = capture.Metadata,
            ["quality"] = new
            {
                g.LeftPlaneRmsMm,
                g.RightPlaneRmsMm,
                g.WingNormalAngleDegrees,
                processed.SelectedComponentRank,
            },
        };
        if (first)
        {
            common["taskId"] = d.TaskId;
            common["thicknessLeft"] = thicknessLeft;
            common["thicknessRight"] = thicknessRight;
        }

        var response = JsonDefaults.Element(common);
        await _store.UpdateAsync(d.TaskId, x =>
        {
            ExpectPhase(x, "Capturing");
            x.Segments.Add(new SegmentDocument
            {
                Number = number,
                SegmentKey = key,
                RequestFingerprint = fingerprint,
                RawPlyPath = rawPly,
                ProcessingResultPath = resultPath,
                CameraPosition = pose,
                CapturedAt = capturedAt,
                Response = response,
            });
            x.Artifacts[artifactId] = new ArtifactDocument
            {
                Path = processed.AbsolutePlyPath,
                ContentType = "application/octet-stream",
                DownloadName = $"{d.TaskId}-segment-{number}.ply",
            };
            x.Artifacts[$"segment-{number:D3}-scanner"] = new ArtifactDocument
            {
                Path = processed.ScannerPlyPath,
                ContentType = "application/octet-stream",
                DownloadName = $"{d.TaskId}-segment-{number}-scanner.ply",
            };
            x.CapturedAt = capturedAt;
            x.Phase = "Collecting";
            x.CoordinatesSnapshot = coordinates;
            if (capture.IsReplay)
            {
                x.IsReplay = true;
            }

            if (first && x.MaterialThicknessMm is null && g.LeftThickness is { } l && g.RightThickness is { } r
                && Math.Abs(l - r) <= Math.Max(1, (l + r) / 2 * .15))
            {
                x.MaterialThicknessMm = (l + r) / 2;
            }
            else if (first && x.MaterialThicknessMm is null && storedThickness is not null)
            {
                x.MaterialThicknessMm = (storedThickness.ThicknessA + storedThickness.ThicknessB) / 2;
            }
        }, ct).ConfigureAwait(false);
        if (first && g.LeftThickness is { } leftMm && g.RightThickness is { } rightMm)
        {
            await _jobThickness.UpsertAsync(new JobThicknessRecord
            {
                JobId = d.JobId ?? "",
                ThicknessA = leftMm,
                ThicknessB = rightMm,
                ScannerId = d.ScannerId,
                MeasuredAt = capturedAt,
                TraceId = requestTraceId ?? d.TraceId,
            }, ct).ConfigureAwait(false);
        }

        var latest = await _store.GetAsync(d.TaskId, "Surface", ct).ConfigureAwait(false);
        return new ScanOutcome { Document = latest, Response = response };
    }

    /// <summary>拍攝高度圖並寫成 raw PLY。</summary>
    /// <remarks>PLY 寫檔來自 Hank Scan3D 的 <c>Scan3D.Scanner.PointCloudExporter</c>。</remarks>
    private async Task<SegmentRawCaptureResult> CaptureRawPlyAsync(
        TaskDocument d,
        int? navigator,
        string directory,
        CancellationToken ct)
    {
        Directory.CreateDirectory(directory);
        var dest = Path.Combine(directory, "raw.ply");
        var useExisting = string.Equals(_configuration["SurfaceScan:UseExistingPly"], "true", StringComparison.OrdinalIgnoreCase);
        var existing = _configuration["SurfaceScan:ExistingPlyPath"];
        if (useExisting && !string.IsNullOrWhiteSpace(existing) && File.Exists(existing))
        {
            File.Copy(existing, dest, overwrite: true);
            return new SegmentRawCaptureResult
            {
                RawPlyPath = dest,
                IsReplay = true,
                Metadata = null,
            };
        }

        if (!_registry.TryGetEndpoint(d.ScannerId, out var endpoint))
        {
            throw new ScanTaskException(400, "CAL_CFG_001", "未知 scannerId。");
        }

        var programNo = _registry.ResolveProgramNo(d.ScannerId, navigator);
        var capture = await _capture.GetCapture(d.ScannerId).CaptureAsync(programNo, ct).ConfigureAwait(false);
        if (!ScanCaptureCache.TryGet(capture.CaptureId, out var height, out var gray))
        {
            throw new InvalidOperationException("擷取高度圖快取已失效，請重新掃描。");
        }

        var metadata = BuildCaptureMetadata(height);
        var frame = HeightImageFrameAdapter.FromLHeightImage(height, gray);
        HeightFramePlyExporter.SavePly(frame, dest);
        ScanCaptureCache.Remove(capture.CaptureId);
        _ = endpoint;
        return new SegmentRawCaptureResult
        {
            RawPlyPath = dest,
            IsReplay = false,
            Metadata = metadata,
        };
    }

    /// <summary>由 LJ 高度圖組 capture 中繼（Hank FrameMetadata）。</summary>
    private static SegmentCaptureMetadata BuildCaptureMetadata(Keyence.LjDevCommon.LHeightImage heightImage)
    {
        return new SegmentCaptureMetadata
        {
            Width = heightImage.XSize,
            ProfileCount = heightImage.YSize,
            PitchXmm = heightImage.CoefficientX,
            PitchYmm = heightImage.CoefficientY,
            PitchZmm = heightImage.CoefficientZ,
            ScannerXStartMm = heightImage.ImagePosition.X,
            ScannerYStartMm = heightImage.ImagePosition.Y,
            CoordinateOrigin = "sdk-origin",
            CoordinateSystem = "scanner",
            Unit = "mm",
        };
    }

    /// <summary>建立任務並佔用掃描器。</summary>
    /// <remarks>來自 Hank Scan3D 的 <c>Scan3D.Service.ScanOrchestrator</c>。</remarks>
    private async Task<TaskDocument> NewTask(
        string kind,
        string scannerId,
        string? jobId,
        string traceId,
        string baseUrl,
        CancellationToken ct)
    {
        Required(scannerId, "scannerId");
        if (!_registry.TryGetEndpoint(scannerId, out var endpoint))
        {
            throw new ScanTaskException(400, "CAL_CFG_001", "未知 scannerId。");
        }

        var coordinates = await CoordinatesForSegmentAsync(scannerId, ct).ConfigureAwait(false);
        if (_surfaceScanFormal.RequireCalibratedCoordinates
            && (kind == "Surface" || kind == "Hole")
            && !coordinates.IsCalibrated)
        {
            throw new ScanTaskException(
                409,
                "COORDINATES_UNCALIBRATED",
                "尚未確認掃描器安裝外參與座標軸，無法發布正式絕對座標。");
        }

        var d = new TaskDocument
        {
            TaskId = kind.ToUpperInvariant() + "-" + Guid.NewGuid().ToString("N"),
            Kind = kind,
            ScannerId = scannerId,
            JobId = jobId,
            TraceId = traceId,
            BaseUrl = baseUrl.TrimEnd('/'),
            CoordinatesSnapshot = coordinates,
        };
        var deviceKey = $"live:{endpoint.Ip}:{endpoint.Port}";
        _devices.Reserve(deviceKey, d.TaskId);
        try
        {
            await _store.CreateAsync(d, ct).ConfigureAwait(false);
        }
        catch
        {
            ReleaseDevice(d.TaskId);
            throw;
        }

        return d;
    }

    /// <summary>讀最新補償組成該段座標轉換。</summary>
    /// <remarks>來自 Hank Scan3D 的 <c>Scan3D.Service.ScanOrchestrator</c>；補償改每段重讀 CompensationHistory。</remarks>
    private async Task<CoordinateTransformOptions> CoordinatesForSegmentAsync(string scannerId, CancellationToken ct)
    {
        CoordinateTransformOptions coordinates;
        if (!_registry.TryGetCoordinateDefaults(scannerId, out coordinates))
        {
            coordinates = new CoordinateTransformOptions();
        }

        var latest = await _compensation.GetLatestAsync(scannerId, ct).ConfigureAwait(false);
        if (latest is null)
        {
            return coordinates;
        }

        var origin = coordinates.WorldOriginOffset;
        var calibrationId = coordinates.CalibrationId;
        if (!string.IsNullOrWhiteSpace(latest.TraceId))
        {
            calibrationId = latest.TraceId;
        }

        return new CoordinateTransformOptions
        {
            ScannerToMount = coordinates.ScannerToMount,
            WorldOriginOffset = new Point3
            {
                X = origin.X + latest.OffsetX,
                Y = origin.Y + latest.OffsetY,
                Z = origin.Z + latest.OffsetZ,
            },
            AAxis = coordinates.AAxis,
            CAxis = coordinates.CAxis,
            ASign = coordinates.ASign,
            CSign = coordinates.CSign,
            ApplyCFirst = coordinates.ApplyCFirst,
            IsCalibrated = coordinates.IsCalibrated,
            CalibrationId = calibrationId,
            InnerReferenceFrame = coordinates.InnerReferenceFrame,
        };
    }

    /// <summary>合併 appsettings Processing 與請求 ROI／rank（Hank CaptureSegment 邏輯）。</summary>
    private static SegmentProcessingOptions MergeSegmentProcessing(
        SegmentProcessingOptions defaults,
        ScannerRoi? roi,
        int? rank)
    {
        return new SegmentProcessingOptions
        {
            Selection = defaults.Selection,
            ComponentRank = rank ?? defaults.ComponentRank,
            ScannerRoi = roi ?? defaults.ScannerRoi,
            ZThresholdMm = defaults.ZThresholdMm,
            CellMm = defaults.CellMm,
            MinimumComponentCells = defaults.MinimumComponentCells,
            MaximumPlaneRmsMm = defaults.MaximumPlaneRmsMm,
            ThicknessLocalRadiusMm = defaults.ThicknessLocalRadiusMm,
            RecoverMeasuredEdges = defaults.RecoverMeasuredEdges,
        };
    }

    /// <summary>組成產物下載 URL。</summary>
    /// <remarks>來自 Hank Scan3D 的 <c>Scan3D.Service.ArtifactService</c>。</remarks>
    private string ArtifactUrl(TaskDocument task, string artifactId)
    {
        return task.BaseUrl + $"/api/files/{task.TaskId}/{artifactId}";
    }

    /// <summary>確認任務階段允許此操作。</summary>
    /// <remarks>來自 Hank Scan3D 的 <c>Scan3D.Service.ScanOrchestrator</c>。</remarks>
    private static void ExpectPhase(TaskDocument d, params string[] allowed)
    {
        if (d.Terminal || !allowed.Contains(d.Phase))
        {
            throw new ScanTaskException(409, "TASK_STATE_CONFLICT", "此任務狀態不允許這個操作。", new { d.Status, d.Phase });
        }
    }

    /// <summary>必填字串檢查。</summary>
    /// <remarks>來自 Hank Scan3D 的 <c>Scan3D.Service.ScanOrchestrator</c>。</remarks>
    private static void Required(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 250)
        {
            throw new ScanTaskException(400, "INVALID_REQUEST", $"{field} 必填且不可超過 250 字元。");
        }
    }

    /// <summary>三維座標檢查。</summary>
    /// <remarks>來自 Hank Scan3D 的 <c>Scan3D.Service.ScanOrchestrator</c>。</remarks>
    private static void Validate(Point3? p, string field)
    {
        if (p is null || new[] { p.X, p.Y, p.Z }.Any(x => !double.IsFinite(x) || Math.Abs(x) > 1e9))
        {
            throw new ScanTaskException(400, "INVALID_REQUEST", $"{field} 需提供有限的 x/y/z，座標絕對值不可超過 10⁹ mm。");
        }
    }

    /// <summary>相機位姿檢查。</summary>
    /// <remarks>來自 Hank Scan3D 的 <c>Scan3D.Service.ScanOrchestrator</c>。</remarks>
    private static void Validate(Pose5? p)
    {
        if (p is null || new[] { p.X, p.Y, p.Z, p.A, p.C }.Any(x => !double.IsFinite(x)))
        {
            throw new ScanTaskException(400, "INVALID_REQUEST", "cameraPosition 需提供有限的 x/y/z/a/c。");
        }

        Validate(new Point3 { X = p.X, Y = p.Y, Z = p.Z }, "cameraPosition");
        if (Math.Abs(p.A) > 1e6 || Math.Abs(p.C) > 1e6)
        {
            throw new ScanTaskException(400, "INVALID_REQUEST", "A/C 角度超出可用範圍（絕對值需小於 10⁶ 度）。");
        }
    }

    /// <summary>掃描器 ROI 檢查。</summary>
    /// <remarks>來自 Hank Scan3D 的 <c>Scan3D.Service.ScanOrchestrator</c>。</remarks>
    private static void ValidateRoi(ScannerRoi? roi)
    {
        if (roi is null)
        {
            return;
        }

        var values = new[] { roi.MinX, roi.MaxX, roi.MinY, roi.MaxY, roi.MinZ, roi.MaxZ };
        if (values.Any(v => !double.IsFinite(v)) || roi.MinX >= roi.MaxX || roi.MinY >= roi.MaxY || roi.MinZ >= roi.MaxZ)
        {
            throw new ScanTaskException(400, "INVALID_ROI", "ROI 必須提供有限座標，且每軸 minimum 小於 maximum。");
        }
    }

    /// <summary>拍攝群組 1～16 檢查。</summary>
    /// <remarks>來自 Hank Scan3D 的 <c>Scan3D.Service.ScanOrchestrator</c>。</remarks>
    private static void ValidateNavigator(int? number)
    {
        if (number is < 1 or > 16)
        {
            throw new ScanTaskException(400, "INVALID_REQUEST", "navigatorScanNo 範圍為 1～16。");
        }
    }

    /// <summary>callbackUrl 與允許主機檢查。</summary>
    /// <remarks>來自 Hank Scan3D 的 <c>Scan3D.Service.ScanOrchestrator</c>。</remarks>
    private void ValidateCallback(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https")
            || !string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new ScanTaskException(400, "INVALID_CALLBACK", "callbackUrl 必須是有效的 HTTP(S) URL。");
        }

        if (!CallbackHostPolicy.IsHostAllowed(_callbackSettings.Mode, uri.Host, _runtimeConfig.CallbackAllowedHosts))
        {
            throw new ScanTaskException(400, "CALLBACK_HOST_NOT_ALLOWED", "callbackUrl 主機不在設定的允許清單。");
        }
    }

    /// <summary>請求指紋（SHA-256）。</summary>
    /// <remarks>來自 Hank Scan3D 的 <c>Scan3D.Service.ScanOrchestrator</c>。</remarks>
    private static string Fingerprint<T>(T value)
    {
        return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value, JsonDefaults.Options)));
    }

    /// <summary>將孔洞特徵點套用剛體轉換（無矩陣則原樣）。</summary>
    private static FeaturePoint MapHolePoint(FeaturePoint point, double[][]? matrix)
    {
        if (matrix is null)
        {
            return point;
        }

        var mapped = RigidTransforms.Apply(matrix, new Point3 { X = point.X, Y = point.Y, Z = point.Z });
        return new FeaturePoint(mapped.X, mapped.Y, mapped.Z);
    }

    /// <summary>將全部孔洞特徵轉成 API／hole-result 用的投影座標。</summary>
    private static object[] MapHoleFeatures(FeatureOpeningModel[] openings, double[][]? matrix)
    {
        var features = new object[openings.Length];
        for (var i = 0; i < openings.Length; i++)
        {
            var f = openings[i];
            features[i] = new
            {
                f.Id,
                f.FaceId,
                f.Type,
                f.Status,
                f.Closed,
                center = MapHolePoint(f.Center, matrix),
                boundary = f.Boundary.Select(p => MapHolePoint(p, matrix)).ToArray(),
                f.ShapeFitRmseMm,
                f.WidthMm,
                f.HeightMm,
                f.Scanlines,
                f.Reason,
            };
        }

        return features;
    }

    /// <summary>掃描器 ROI 轉孔洞特徵 ROI。</summary>
    /// <remarks>對應 Hank Scan3D 的 <c>Scan3D.Features.FeatureRoi</c>。</remarks>
    private static FeatureRoi? ToFeatureRoi(ScannerRoi? roi)
    {
        if (roi is null)
        {
            return null;
        }

        return new FeatureRoi(
            new FeaturePoint(roi.MinX, roi.MinY, roi.MinZ),
            new FeaturePoint(roi.MaxX, roi.MaxY, roi.MaxZ));
    }

    /// <summary>打包 STEP／glTF／報告與三視角 PNG 成 ZIP。</summary>
    /// <remarks>來自 Hank Scan3D 的 <c>Scan3D.Service.ArtifactService.BuildAttachments</c>。</remarks>
    private Task<string?> BuildAttachmentsAsync(
        TaskDocument task,
        StitchProcessingResult result,
        CancellationToken ct)
    {
        return StitchAttachmentPackager.BuildAsync(task.TaskId, result, ct);
    }

    private void WriteStatus(string phase, TaskDocument d, int? seq, string? message = null)
    {
        try
        {
            if (!_registry.TryGetEndpoint(d.ScannerId, out var endpoint))
            {
                return;
            }

            var path = ScanMonitorPaths.StatusFile(_storage.ContentsRoot, endpoint.MachineArea);
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var json = JsonSerializer.Serialize(new
            {
                Phase = phase,
                d.TaskId,
                d.JobId,
                Seq = seq,
                Message = message ?? phase,
                UpdatedAt = DateTimeOffset.Now,
            }, JsonDefaults.Options);
            File.WriteAllText(path, json);
        }
        catch
        {
        }
    }

    private void WriteGlbPointer(TaskDocument d, string? glbPath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(glbPath) || !_registry.TryGetEndpoint(d.ScannerId, out var endpoint))
            {
                return;
            }

            var pointer = ScanMonitorPaths.GlbPointerFile(_storage.ContentsRoot, endpoint.MachineArea);
            if (string.IsNullOrWhiteSpace(pointer))
            {
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(pointer)!);
            File.WriteAllText(pointer, glbPath);
        }
        catch
        {
        }
    }

    private void WriteHoleBoundary(TaskDocument d, DateTimeOffset capturedAt, FeaturePoint[] boundary)
    {
        try
        {
            var path = ScanMonitorPaths.HoleBoundaryFile(_storage.ContentsRoot);
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(new
            {
                d.TaskId,
                d.JobId,
                scanId = "SCAN-" + d.TaskId,
                capturedAt,
                featureBoundary = boundary,
                updatedAt = DateTimeOffset.Now,
            }, JsonDefaults.Options));
        }
        catch
        {
        }
    }
}
