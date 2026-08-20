using System.Text.Json;
using Microsoft.Extensions.Options;
using ThreeDScan.Application.Calibration.Models;
using ThreeDScan.Application.Common;
using ThreeDScan.Domain.Abstractions;
using ThreeDScan.Domain.Models;
using ThreeDScan.Domain.Repositories;

namespace ThreeDScan.Application.Calibration;

/// <summary>校正應用服務：UC-01 儀器校正、Ref 建立與 WPF 預覽推送。</summary>
public sealed class CalibrationAppService : ICalibrationAppService
{
    private readonly IScannerRegistry _registry;
    private readonly IScannerConfigRepository _configRepository;
    private readonly ICompensationRepository _compensationRepository;
    private readonly IOperationLogRepository _operationLogRepository;
    private readonly IInstrumentCalibrationTaskRepository _taskRepository;
    private readonly IScannerCaptureFactory _captureFactory;
    private readonly ILjSphereMeasureEngine _measureEngine;
    private readonly ICalibrationPreviewPublisher _previewPublisher;
    private readonly LjCalibrationParameters _calibrationParams;
    private readonly CalibrationSettings _calibrationSettings;
    private readonly IStorageSettings _storage;
    private readonly InstrumentCalibrationService _instrumentCalibration;

    /// <summary>建立校正應用服務（依賴注入）。</summary>
    public CalibrationAppService(
        IScannerRegistry registry,
        IScannerConfigRepository configRepository,
        ICompensationRepository compensationRepository,
        IOperationLogRepository operationLogRepository,
        IInstrumentCalibrationTaskRepository taskRepository,
        IScannerCaptureFactory captureFactory,
        ILjSphereMeasureEngine measureEngine,
        ICalibrationPreviewPublisher previewPublisher,
        IOptions<LjCalibrationParameters> calibrationParams,
        IOptions<CalibrationSettings> calibrationSettings,
        IStorageSettings storage,
        InstrumentCalibrationService? instrumentCalibration = null)
    {
        _registry = registry;
        _configRepository = configRepository;
        _compensationRepository = compensationRepository;
        _operationLogRepository = operationLogRepository;
        _taskRepository = taskRepository;
        _captureFactory = captureFactory;
        _measureEngine = measureEngine;
        _previewPublisher = previewPublisher;
        _calibrationParams = calibrationParams.Value;
        _calibrationSettings = calibrationSettings.Value;
        _storage = storage;
        _instrumentCalibration = instrumentCalibration ?? new InstrumentCalibrationService();
    }

    /// <inheritdoc />
    /// <remarks>
    /// 兩次拍攝：final=false 建立 task 並回 seq=1；final=true 合併並依品質寫補償或 CAL_002。
    /// 第一次成功不檢查 RMSE 門檻；球數不足在 CaptureAndComputeFrameAsync 就失敗。
    /// </remarks>
    public async Task<ApiResponse<InstrumentCalibrationData>> CalibrateInstrumentAsync(
        InstrumentCalibrationRequest request,
        CancellationToken ct = default)
    {
        var traceId = Guid.NewGuid().ToString("N");
        ScannerCaptureResult? capture = null;
        ScannerConfigRecord? config = null;
        string? previewGrayPath = null;
        string? previewHeightPath = null;

        try
        {
            // 契約：兩次拍攝的 final / taskId 組合必須成對。
            if (request.Final && string.IsNullOrWhiteSpace(request.TaskId))
            {
                return await InstrumentFailAsync(
                    request, traceId, "CAL_001",
                    "儀器校正須兩次拍攝：第一次 final=false（取得 taskId），平台移動後帶 taskId 且 final=true。",
                    null, ct);
            }

            if (!request.Final && !string.IsNullOrWhiteSpace(request.TaskId))
            {
                return await InstrumentFailAsync(
                    request, traceId, "CAL_001",
                    "第一次拍攝請省略 taskId，並設 final=false。",
                    null, ct);
            }

            if (!_registry.TryGetEndpoint(request.ScannerId, out var endpoint))
                return await InstrumentFailAsync(request, traceId, "CAL_CFG_001", $"未知 ScannerId: {request.ScannerId}", null, ct);

            var programNo = _registry.ResolveProgramNo(request.ScannerId, request.NavigatorScanNo ?? endpoint.NavigatorScanNo);

            // Ref 影像與種子都在 ScannerConfig；沒種子不能量。
            config = await _configRepository.GetAsync(request.ScannerId, ct);
            if (config is null)
                return await InstrumentFailAsync(request, traceId, "CAL_CFG_003",
                    $"找不到 ScannerConfig（{request.ScannerId}）。請先執行 seed-scannerconfig.sql，再執行 Ref 建立 API。", null, ct);

            // 第二次拍攝：task 必須仍是 Pending，且球數與第一次相同。
            InstrumentCalibrationTaskRecord? pendingTask = null;
            if (request.Final)
            {
                pendingTask = await _taskRepository.GetAsync(request.TaskId!, ct);
                var taskError = ValidatePendingTask(pendingTask, request);
                if (taskError is not null)
                {
                    var (code, message) = taskError.Value;
                    return await InstrumentFailAsync(request, traceId, code, message, null, ct);
                }
            }

            // 拍攝 + PatternMatch 尋球 + 與台船 known 座標配對計算補償。
            var frame = await CaptureAndComputeFrameAsync(
                request, config, programNo, traceId, ct);
            if (frame.Error is not null)
            {
                var (errorCode, errorMessage) = frame.Error.Value;
                capture = frame.Capture;
                previewGrayPath = frame.PreviewGrayPath;
                previewHeightPath = frame.PreviewHeightPath;
                await PublishPreviewAsync(
                    request.ScannerId, traceId, false, frame.Balls, config, capture,
                    previewGrayPath, previewHeightPath,
                    null, null, null, null, errorMessage, ct,
                    frame.MeasureParams, frame.DetectCount);
                return await InstrumentFailAsync(
                    request, traceId, errorCode, errorMessage,
                    frame.DiagnosticData, ct);
            }

            capture = frame.Capture;
            previewGrayPath = frame.PreviewGrayPath;
            previewHeightPath = frame.PreviewHeightPath;

            if (!request.Final)
                return await BeginTaskAsync(request, traceId, frame, config, ct);

            return await FinalizeTaskAsync(request, traceId, pendingTask!, frame, config, endpoint.MachineArea, ct);
        }
        catch (InstrumentCalibrationException ex)
        {
            await PublishPreviewAsync(
                request.ScannerId, traceId, false, Array.Empty<MeasuredBall>(), config, capture,
                previewGrayPath, previewHeightPath,
                null, null, null, null, ex.Message, ct);
            return await InstrumentFailAsync(request, traceId, ex.ErrorCode, ex.Message, null, ct);
        }
        catch (NotImplementedException ex)
        {
            await PublishPreviewAsync(
                request.ScannerId, traceId, false, Array.Empty<MeasuredBall>(), config, capture,
                previewGrayPath, previewHeightPath,
                null, null, null, null, ex.Message, ct);
            return await InstrumentFailAsync(request, traceId, "CAL_SDK_001", ex.Message, null, ct);
        }
        catch (Exception ex)
        {
            await PublishPreviewAsync(
                request.ScannerId, traceId, false, Array.Empty<MeasuredBall>(), config, capture,
                previewGrayPath, previewHeightPath,
                null, null, null, null, ex.Message, ct);
            return await InstrumentFailAsync(request, traceId, "CAL_SYS_001", ex.Message, null, ct);
        }
    }

    /// <summary>第二次拍攝前檢查 task 是否仍可合併（存在、同掃描器、未結束、未逾時、球數一致）。</summary>
    private static (string Code, string Message)? ValidatePendingTask(
        InstrumentCalibrationTaskRecord? task,
        InstrumentCalibrationRequest request)
    {
        // 規格 CAL_001：taskId 不存在或已逾時（其餘契約錯誤一併對齊此碼）
        if (task is null)
            return ("CAL_001", $"找不到校正任務 taskId={request.TaskId}。");

        if (!string.Equals(task.ScannerId, request.ScannerId, StringComparison.OrdinalIgnoreCase))
            return ("CAL_001", "taskId 與 scannerId 不符。");

        if (!string.Equals(task.Status, InstrumentCalibrationTaskRecord.StatusPending, StringComparison.Ordinal))
            return ("CAL_001", $"校正任務已結束（Status={task.Status}），請重新開始第一次拍攝。");

        if (task.ExpiresAt <= DateTimeOffset.Now)
            return ("CAL_001", "校正任務已逾時，請重新開始第一次拍攝。");

        if (task.BallCount != request.StandardBalls.Count)
            return ("CAL_001",
                $"第二次 standardBalls 數量（{request.StandardBalls.Count}）須與第一次（{task.BallCount}）相同。");

        return null;
    }

    /// <summary>
    /// 拍一張、跑 SDK 尋球、與 standardBalls 配對算出本幀補償。
    /// KeptCount=0 或配對球數不足會帶 Error，不進 task。
    /// </summary>
    private async Task<FrameComputeResult> CaptureAndComputeFrameAsync(
        InstrumentCalibrationRequest request,
        ScannerConfigRecord config,
        byte programNo,
        string traceId,
        CancellationToken ct)
    {
        var capture = await _captureFactory.GetCapture(request.ScannerId).CaptureAsync(programNo, ct);
        var previewPaths = SavePreviewImages(capture, request.ScannerId, programNo, traceId);

        var effectiveParams = LjCalibrationMerge.ForScanner(config, _calibrationParams);
        var detectCount = Math.Clamp(request.StandardBalls.Count, 1, 10);
        var measureParams = WithDetectCount(effectiveParams, detectCount);

        var measure = await _measureEngine.MeasureAsync(request.ScannerId, config, capture, measureParams, ct);

        // SDK 一顆都沒留：CAL_FEED_001（與「有抓到但少於台船顆數」同一碼，訊息不同）。
        if (measure.KeptCount == 0)
        {
            var feedMsg =
                $"標準球未偵測到或無法識別（需 {detectCount} 顆，量得 0 顆；" +
                $"ExpectedBallDiameter={measureParams.ExpectedBallDiameter:F1} mm）。";
            return new FrameComputeResult
            {
                Capture = capture,
                PreviewGrayPath = previewPaths.GrayRelativePath,
                PreviewHeightPath = previewPaths.HeightRelativePath,
                Balls = measure.Balls,
                MeasureParams = measureParams,
                DetectCount = detectCount,
                Error = ("CAL_FEED_001", feedMsg),
            };
        }

        var pairingBalls = SelectBallsForPairing(measure.Balls, request.StandardBalls.Count);

        InstrumentCalibrationComputeResult computed;
        try
        {
            computed = _instrumentCalibration.Compute(
                request.CameraExpectedPosition,
                request.StandardBalls,
                pairingBalls);
        }
        catch (InstrumentCalibrationException ex)
        {
            return new FrameComputeResult
            {
                Capture = capture,
                PreviewGrayPath = previewPaths.GrayRelativePath,
                PreviewHeightPath = previewPaths.HeightRelativePath,
                Balls = measure.Balls,
                MeasureParams = measureParams,
                DetectCount = detectCount,
                Error = (ex.ErrorCode, ex.Message),
            };
        }

        return new FrameComputeResult
        {
            Capture = capture,
            PreviewGrayPath = previewPaths.GrayRelativePath,
            PreviewHeightPath = previewPaths.HeightRelativePath,
            Balls = measure.Balls,
            MeasureParams = measureParams,
            DetectCount = detectCount,
            Computed = computed,
            DatumPlane = measure.DatumPlane,
        };
    }

    /// <summary>第一次拍攝：存 Pending task，回 seq=1 / AwaitingNextShot。此步不擋 RMSE。</summary>
    private async Task<ApiResponse<InstrumentCalibrationData>> BeginTaskAsync(
        InstrumentCalibrationRequest request,
        string traceId,
        FrameComputeResult frame,
        ScannerConfigRecord config,
        CancellationToken ct)
    {
        var computed = frame.Computed!;
        var now = DateTimeOffset.Now;
        var ttl = Math.Max(1, _calibrationSettings.InstrumentTaskTtlMinutes);
        var taskId = Guid.NewGuid().ToString("N");

        await _taskRepository.CreateAsync(new InstrumentCalibrationTaskRecord
        {
            TaskId = taskId,
            ScannerId = request.ScannerId,
            Status = InstrumentCalibrationTaskRecord.StatusPending,
            CreatedAt = now,
            UpdatedAt = now,
            ExpiresAt = now.AddMinutes(ttl),
            ExpectedX = request.CameraExpectedPosition.X,
            ExpectedY = request.CameraExpectedPosition.Y,
            ExpectedZ = request.CameraExpectedPosition.Z,
            BallCount = request.StandardBalls.Count,
            ScannerToFloorZ = computed.ScannerToFloorZ,
            ActualX = computed.ActualScannerPosition.X,
            ActualY = computed.ActualScannerPosition.Y,
            Rmse = computed.Rmse,
            FrameJson = JsonSerializer.Serialize(new
            {
                matchedBalls = computed.MatchedBalls,
                scannerToFloorZ = computed.ScannerToFloorZ,
                rmse = computed.Rmse,
            }),
            TraceIdSeq1 = traceId,
        }, ct);

        var data = BuildInstrumentData(
            request, traceId, frame, computed,
            taskId: taskId,
            seq: 1,
            zDeviation: null,
            calibratedAt: now,
            status: InstrumentCalibrationData.StatusAwaitingNextShot);

        await PublishPreviewAsync(
            request.ScannerId, traceId, true, frame.Balls, config, frame.Capture,
            frame.PreviewGrayPath, frame.PreviewHeightPath,
            computed.Compensation.OffsetX, computed.Compensation.OffsetY, computed.Compensation.OffsetZ,
            computed.Rmse, null, ct,
            frame.MeasureParams, frame.DetectCount, computed.MatchedBalls);

        return await InstrumentOkAsync(request, traceId, data, ct);
    }

    /// <summary>
    /// 第二次拍攝：與 seq=1 合併 XY／Z，品質過才寫 CompensationHistory。
    /// 品質未過回 CAL_002，task 仍標記完成（不可重用同一 taskId）。
    /// </summary>
    private async Task<ApiResponse<InstrumentCalibrationData>> FinalizeTaskAsync(
        InstrumentCalibrationRequest request,
        string traceId,
        InstrumentCalibrationTaskRecord seq1,
        FrameComputeResult frame,
        ScannerConfigRecord config,
        string machineArea,
        CancellationToken ct)
    {
        var seq2 = frame.Computed!;
        var merged = InstrumentTwoShotMerge.Merge(
            seq1.ScannerToFloorZ,
            seq1.ActualX,
            seq1.ActualY,
            seq1.Rmse,
            seq2,
            request.CameraExpectedPosition,
            _calibrationSettings.ZDeviationThresholdMm);

        var now = DateTimeOffset.Now;
        await _taskRepository.CompleteAsync(seq1.TaskId, ct);

        // 規格：品質未過 → 不回成功結構，直接 CAL_002；原補償不受影響。
        if (!merged.QualityOk)
        {
            var failMsg =
                $"品質未通過（rmseXY={merged.RmseXY:F3}, zDeviation={merged.ZDeviation:F3}），未寫入補償。";
            await PublishPreviewAsync(
                request.ScannerId, traceId, false, frame.Balls, config, frame.Capture,
                frame.PreviewGrayPath, frame.PreviewHeightPath,
                merged.Compensation.OffsetX, merged.Compensation.OffsetY, merged.Compensation.OffsetZ,
                merged.RmseXY, failMsg, ct,
                frame.MeasureParams, frame.DetectCount, seq2.MatchedBalls);

            var diagnostic = new InstrumentCalibrationData
            {
                ScannerId = request.ScannerId,
                TraceId = traceId,
                Compensation = merged.Compensation,
                ActualScannerPosition = merged.ActualScannerPosition,
                CalibratedAt = now.ToString("O"),
                Balls = frame.Balls
                    .Where(b => b.IsWithinTolerance)
                    .Select(b => new MeasuredBallDto
                    {
                        Index = b.Index,
                        X = b.X,
                        Y = b.Y,
                        Z = b.Z,
                        Diameter = b.Diameter,
                        Ok = true,
                    }).ToList(),
                MatchedBalls = seq2.MatchedBalls,
                DatumPlane = MapDatumPlane(frame.DatumPlane),
                ScannerToFloorZ = merged.ScannerToFloorZ,
                RmseXY = merged.RmseXY,
                TaskId = seq1.TaskId,
                Seq = 2,
                ZDeviation = merged.ZDeviation,
            };
            return await InstrumentFailAsync(request, traceId, "CAL_002", failMsg, diagnostic, ct);
        }

        // 品質通過才寫入補償；MachineArea 來自 appsettings 該掃描器設定。
        await _compensationRepository.AppendAsync(new CompensationOffset
        {
            ScannerId = request.ScannerId,
            OffsetX = merged.Compensation.OffsetX,
            OffsetY = merged.Compensation.OffsetY,
            OffsetZ = merged.Compensation.OffsetZ,
            Rmse = merged.RmseXY,
            MachineArea = machineArea,
            CalibratedAt = now,
            Operator = request.Operator,
            TraceId = traceId,
        }, ct);

        var data = new InstrumentCalibrationData
        {
            ScannerId = request.ScannerId,
            TraceId = traceId,
            Compensation = merged.Compensation,
            ActualScannerPosition = merged.ActualScannerPosition,
            CalibratedAt = now.ToString("O"),
            Balls = frame.Balls
                .Where(b => b.IsWithinTolerance)
                .Select(b => new MeasuredBallDto
                {
                    Index = b.Index,
                    X = b.X,
                    Y = b.Y,
                    Z = b.Z,
                    Diameter = b.Diameter,
                    Ok = true,
                }).ToList(),
            MatchedBalls = seq2.MatchedBalls,
            DatumPlane = MapDatumPlane(frame.DatumPlane),
            ScannerToFloorZ = merged.ScannerToFloorZ,
            RmseXY = merged.RmseXY,
            TaskId = seq1.TaskId,
            Seq = 2,
            Status = null,
            ZDeviation = merged.ZDeviation,
        };

        await PublishPreviewAsync(
            request.ScannerId, traceId, true, frame.Balls, config, frame.Capture,
            frame.PreviewGrayPath, frame.PreviewHeightPath,
            merged.Compensation.OffsetX, merged.Compensation.OffsetY, merged.Compensation.OffsetZ,
            merged.RmseXY, null, ct,
            frame.MeasureParams, frame.DetectCount, seq2.MatchedBalls);

        return await InstrumentOkAsync(request, traceId, data, ct);
    }

    /// <summary>組出給客戶／監看用的儀器校正 data（含量測球、補償、task 狀態）。</summary>
    private static InstrumentCalibrationData BuildInstrumentData(
        InstrumentCalibrationRequest request,
        string traceId,
        FrameComputeResult frame,
        InstrumentCalibrationComputeResult computed,
        string taskId,
        int seq,
        double? zDeviation,
        DateTimeOffset calibratedAt,
        string? status)
    {
        return new()
        {
            ScannerId = request.ScannerId,
            TraceId = traceId,
            Compensation = computed.Compensation,
            ActualScannerPosition = computed.ActualScannerPosition,
            CalibratedAt = calibratedAt.ToString("O"),
            Balls = frame.Balls
                .Where(b => b.IsWithinTolerance)
                .Select(b => new MeasuredBallDto
                {
                    Index = b.Index,
                    X = b.X,
                    Y = b.Y,
                    Z = b.Z,
                    Diameter = b.Diameter,
                    Ok = true,
                }).ToList(),
            MatchedBalls = computed.MatchedBalls,
            DatumPlane = MapDatumPlane(frame.DatumPlane),
            ScannerToFloorZ = computed.ScannerToFloorZ,
            RmseXY = computed.Rmse,
            TaskId = taskId,
            Seq = seq,
            Status = status,
            ZDeviation = zDeviation,
        };
    }

    /// <summary>單次拍攝的中間結果：影像路徑、SDK 球、配對計算、或本幀錯誤。</summary>
    private sealed class FrameComputeResult
    {
        public ScannerCaptureResult? Capture { get; init; }
        public string? PreviewGrayPath { get; init; }
        public string? PreviewHeightPath { get; init; }
        public IReadOnlyList<MeasuredBall> Balls { get; init; } = Array.Empty<MeasuredBall>();
        public LjCalibrationParameters? MeasureParams { get; init; }
        public int? DetectCount { get; init; }
        public InstrumentCalibrationComputeResult? Computed { get; init; }
        public DatumPlaneMeasureResult? DatumPlane { get; init; }
        public (string Code, string Message)? Error { get; init; }
        public InstrumentCalibrationData? DiagnosticData { get; init; }
    }

    /// <inheritdoc />
    /// <remarks>
    /// 步驟：讀 DB 種子 → 再拍一張（非測試掃描那張）→ SDK 尋球 → 成功才 Upsert。
    /// Ref X/Y 寫的是尋球結果，不是 existing 裡手動填的值；ExpectedBallDiameter 則刻意不覆寫。
    /// </remarks>
    public async Task<ApiResponse<ReferenceSetupData>> SetupReferenceAsync(
        ReferenceSetupRequest request,
        CancellationToken ct = default)
    {
        var traceId = Guid.NewGuid().ToString("N");
        try
        {
            if (!_registry.TryGetEndpoint(request.ScannerId, out var endpoint))
                return await ReferenceFailAsync(request, traceId, "CAL_CFG_001", $"未知 ScannerId: {request.ScannerId}", ct);

            // Navigator Scan No → SDK 程式號；內容目錄 = ContentsRoot/{ScannerId}。
            var programNo = _registry.ResolveProgramNo(request.ScannerId, request.NavigatorScanNo ?? endpoint.NavigatorScanNo);
            var contentsFolder = _registry.ResolveScannerContentsFolder(request.ScannerId, _storage.ContentsRoot);
            var existing = await _configRepository.GetAsync(request.ScannerId, ct);
            var effectiveParams = LjCalibrationMerge.ForScanner(existing, _calibrationParams);

            // 再拍攝：測試掃描座標不會帶進來，只透過 existing.Ref* 當種子。
            var capture = await _captureFactory.GetCapture(request.ScannerId).CaptureAsync(programNo, ct);
            var setup = await _measureEngine.SetupReferenceAsync(
                request.ScannerId, existing, capture, effectiveParams, contentsFolder, ct);

            // 組要寫回 DB 的一列：Ref XY 用尋球結果；其餘校正參數沿用 existing。
            // ExpectedBallDiameter 不以本次 discoveredDiameter 覆寫（噪聲會把錯誤標準鎖進 instrument）。
            var record = new ScannerConfigRecord
            {
                ScannerId = request.ScannerId,
                // 尋球擬合球心；現場手動填的種子若與擬合點不同，此處會被蓋掉。
                RefPatternX = setup.RefPatternX,
                RefPatternY = setup.RefPatternY,
                RefSphereX = setup.RefSphereX,
                RefSphereY = setup.RefSphereY,
                ContentsFolder = setup.ContentsFolder,
                UpdatedAt = DateTimeOffset.Now,
                UpdatedBy = request.Operator,
                TraceId = traceId,
                ExpectedBallDiameter = existing?.ExpectedBallDiameter ?? effectiveParams.ExpectedBallDiameter,
                DiameterTolerance = existing?.DiameterTolerance,
                MaxDiameterError = existing?.MaxDiameterError,
                BallCount = existing?.BallCount,
                DatumPlaneEnabled = existing?.DatumPlaneEnabled,
                DatumPlaneCenterX = existing?.DatumPlaneCenterX,
                DatumPlaneCenterY = existing?.DatumPlaneCenterY,
                DatumPlaneWidth = existing?.DatumPlaneWidth,
                DatumPlaneHeight = existing?.DatumPlaneHeight,
                DatumPlaneAngle = existing?.DatumPlaneAngle,
            };
            await _configRepository.UpsertAsync(record, ct);

            await LogOperationAsync(
                traceId, request.Operator, "SetupReference", request.ScannerId,
                success: true, errorCode: null, detail: setup.Message, ct);

            // HTTP 只回 RefPattern 與直徑；RefSphere 有進 DB，不在 JSON。
            return ApiResponse<ReferenceSetupData>.Ok(new ReferenceSetupData
            {
                ScannerId = request.ScannerId,
                TraceId = traceId,
                ProgramNo = programNo,
                RefPatternX = setup.RefPatternX,
                RefPatternY = setup.RefPatternY,
                DiscoveredDiameter = setup.DiscoveredDiameter,
                ContentsFolder = setup.ContentsFolder,
                Message = setup.Message,
            });
        }
        catch (NotImplementedException ex)
        {
            return await ReferenceFailAsync(request, traceId, "CAL_SDK_001", ex.Message, ct);
        }
        catch (Exception ex)
        {
            return await ReferenceFailAsync(request, traceId, "CAL_SYS_001", ex.Message, ct);
        }
    }

    /// <summary>推送儀器校正結果至 SignalR（WPF 校正監看）。</summary>
    private async Task PublishPreviewAsync(
        string scannerId,
        string traceId,
        bool success,
        IReadOnlyList<MeasuredBall> balls,
        ScannerConfigRecord? config,
        ScannerCaptureResult? capture,
        string? previewGrayRelativePath,
        string? previewHeightRelativePath,
        double? ox,
        double? oy,
        double? oz,
        double? rmse,
        string? error,
        CancellationToken ct,
        LjCalibrationParameters? measureParams = null,
        int? detectCount = null,
        IReadOnlyList<MatchedBallDto>? matchedBalls = null)
    {
        await _previewPublisher.PublishAsync(ApplyPreviewMode(new CalibrationPreview
        {
            ScannerId = scannerId,
            TraceId = traceId,
            Timestamp = DateTimeOffset.Now,
            Success = success,
            ErrorMessage = error,
            Balls = balls.Where(b => b.IsWithinTolerance).ToList(),
            CompensationOffsetX = ox,
            CompensationOffsetY = oy,
            CompensationOffsetZ = oz,
            Rmse = rmse,
            PreviewGrayRelativePath = previewGrayRelativePath,
            PreviewHeightRelativePath = previewHeightRelativePath,
            ContentsFolder = config?.ContentsFolder,
            Geometry = capture?.Geometry,
            RefPatternX = config?.RefPatternX,
            RefPatternY = config?.RefPatternY,
            RefSphereX = config?.RefSphereX,
            RefSphereY = config?.RefSphereY,
            ExpectedBallDiameter = measureParams?.ExpectedBallDiameter,
            MaxDiameterError = measureParams?.MaxDiameterError,
            DiameterTolerance = measureParams?.DiameterTolerance,
            DetectCount = detectCount,
            MatchedBalls = MapMatchedBalls(matchedBalls),
        }), ct);
    }

    /// <summary>Production 模式剝掉監看用的球清單／Ref，只留補償與成敗。</summary>
    private CalibrationPreview ApplyPreviewMode(CalibrationPreview preview)
    {
        if (_calibrationSettings.IsDebugResponse)
            return preview;

        return new CalibrationPreview
        {
            ScannerId = preview.ScannerId,
            TraceId = preview.TraceId,
            Timestamp = preview.Timestamp,
            Success = preview.Success,
            ErrorMessage = preview.ErrorMessage,
            CompensationOffsetX = preview.CompensationOffsetX,
            CompensationOffsetY = preview.CompensationOffsetY,
            CompensationOffsetZ = preview.CompensationOffsetZ,
            Rmse = preview.Rmse,
        };
    }

    /// <summary>配對表轉成 SignalR 預覽用的精簡座標。</summary>
    private static IReadOnlyList<MatchedBallPreview> MapMatchedBalls(IReadOnlyList<MatchedBallDto>? matched)
    {
        if (matched is null || matched.Count == 0)
            return Array.Empty<MatchedBallPreview>();

        return matched.Select(m => new MatchedBallPreview
        {
            BallId = m.BallId,
            KnownX = m.KnownX,
            KnownY = m.KnownY,
            MeasuredX = m.MeasuredX,
            MeasuredY = m.MeasuredY,
            ResidualMm = m.ResidualMm,
        }).ToList();
    }

    /// <summary>將本次掃描影像複製至 ContentsRoot 預覽目錄，供靜態檔與 3D 監看使用。</summary>
    private InstrumentPreviewPaths SavePreviewImages(
        ScannerCaptureResult capture,
        string scannerId,
        byte programNo,
        string traceId)
    {
        var dir = Path.Combine(_storage.ContentsRoot, scannerId, $"program_{programNo}", "previews");
        Directory.CreateDirectory(dir);

        string? grayRel = null;
        string? heightRel = null;

        if (!string.IsNullOrEmpty(capture.GrayImagePath) && File.Exists(capture.GrayImagePath))
        {
            var grayName = $"{traceId}_gray.png";
            File.Copy(capture.GrayImagePath, Path.Combine(dir, grayName), overwrite: true);
            grayRel = Path.Combine(scannerId, $"program_{programNo}", "previews", grayName).Replace('\\', '/');
        }

        if (!string.IsNullOrEmpty(capture.HeightImagePath) && File.Exists(capture.HeightImagePath))
        {
            var heightName = $"{traceId}_height.png";
            File.Copy(capture.HeightImagePath, Path.Combine(dir, heightName), overwrite: true);
            heightRel = Path.Combine(scannerId, $"program_{programNo}", "previews", heightName).Replace('\\', '/');
        }

        return new InstrumentPreviewPaths
        {
            GrayRelativePath = grayRel,
            HeightRelativePath = heightRel,
        };
    }

    /// <summary>基準面量測結果轉 API DTO；沒量到則 null。</summary>
    private static DatumPlaneDto? MapDatumPlane(DatumPlaneMeasureResult? plane)
    {
        if (plane is null) return null;
        return new DatumPlaneDto
        {
            Ok = plane.Ok,
            ErrorMessage = plane.ErrorMessage,
            PlaneA = plane.PlaneA,
            PlaneB = plane.PlaneB,
            PlaneC = plane.PlaneC,
            PlaneZAtRegionCenter = plane.PlaneZAtRegionCenter,
            FirstBallRelativeZ = plane.FirstBallRelativeZ,
        };
    }

    /// <summary>配對用球清單：優先直徑合格球，數量仍不足時退回全部量測球。</summary>
    private static IReadOnlyList<MeasuredBall> SelectBallsForPairing(
        IReadOnlyList<MeasuredBall> balls,
        int needed)
    {
        var ok = balls.Where(b => b.IsWithinTolerance).ToList();
        if (ok.Count >= needed)
            return ok;
        return balls;
    }

    /// <summary>寫 OperationLog 後回成功；data 再依 Production／Debug 裁欄位。</summary>
    private async Task<ApiResponse<InstrumentCalibrationData>> InstrumentOkAsync(
        InstrumentCalibrationRequest request,
        string traceId,
        InstrumentCalibrationData data,
        CancellationToken ct)
    {
        await LogOperationAsync(
            traceId, request.Operator, "CalibrateInstrument", request.ScannerId,
            success: true, errorCode: null,
            detail: $"seq={data.Seq} status={data.Status} offset=({data.Compensation?.OffsetX:F3},{data.Compensation?.OffsetY:F3},{data.Compensation?.OffsetZ:F3}) rmseXY={data.RmseXY:F3}"
                    + (data.ZDeviation is double rz ? $" zDeviation={rz:F3}" : ""),
            ct);
        return ApiResponse<InstrumentCalibrationData>.Ok(ApplyResponseMode(data));
    }

    /// <summary>寫失敗稽核。Debug 且有 diagnostic 時把量測球一併帶回，Production 只回錯誤碼。</summary>
    private async Task<ApiResponse<InstrumentCalibrationData>> InstrumentFailAsync(
        InstrumentCalibrationRequest request,
        string traceId,
        string errorCode,
        string errorMessage,
        InstrumentCalibrationData? diagnosticData,
        CancellationToken ct)
    {
        await LogOperationAsync(
            traceId, request.Operator, "CalibrateInstrument", request.ScannerId,
            success: false, errorCode, errorMessage, ct);

        if (_calibrationSettings.IsDebugResponse && diagnosticData is not null)
            return ApiResponse<InstrumentCalibrationData>.Fail(errorCode, errorMessage, ApplyResponseMode(diagnosticData));

        return ApiResponse<InstrumentCalibrationData>.Fail(errorCode, errorMessage);
    }

    /// <summary>Ref 失敗：只寫稽核，不推 WPF（與 instrument 不同）。</summary>
    private async Task<ApiResponse<ReferenceSetupData>> ReferenceFailAsync(
        ReferenceSetupRequest request,
        string traceId,
        string errorCode,
        string errorMessage,
        CancellationToken ct)
    {
        await LogOperationAsync(
            traceId, request.Operator, "SetupReference", request.ScannerId,
            success: false, errorCode, errorMessage, ct);
        return ApiResponse<ReferenceSetupData>.Fail(errorCode, errorMessage);
    }

    /// <summary>寫入 dbo.OperationLog（CalibrateInstrument / SetupReference）。</summary>
    private Task LogOperationAsync(
        string traceId,
        string? operatorName,
        string action,
        string target,
        bool success,
        string? errorCode,
        string? detail,
        CancellationToken ct)
    {
        return _operationLogRepository.AppendAsync(new OperationLogEntry
        {
            Timestamp = DateTimeOffset.Now,
            TraceId = traceId,
            ClientId = _calibrationSettings.ClientId,
            Operator = operatorName,
            Action = action,
            Target = target,
            Result = success ? "Success" : "Fail",
            ErrorCode = errorCode,
            Detail = detail,
        }, ct);
    }

    /// <summary>
    /// Production 只回規格欄位：seq=1 為 taskId／status；seq=2 為補償／rmseXY／zDeviation。
    /// Debug 回完整量測球與中間值。
    /// </summary>
    private InstrumentCalibrationData ApplyResponseMode(InstrumentCalibrationData data)
    {
        if (_calibrationSettings.IsDebugResponse)
            return data;

        // 規格 4.3：第一次 taskId / seq / status / traceId
        if (data.Seq == 1)
        {
            return new InstrumentCalibrationData
            {
                TaskId = data.TaskId,
                Seq = data.Seq,
                Status = data.Status ?? InstrumentCalibrationData.StatusAwaitingNextShot,
                TraceId = data.TraceId,
            };
        }

        // 規格 4.4：第二次 scannerId / compensation / rmseXY / zDeviation / calibratedAt / traceId
        return new InstrumentCalibrationData
        {
            ScannerId = data.ScannerId,
            Compensation = data.Compensation,
            RmseXY = data.RmseXY,
            ZDeviation = data.ZDeviation,
            CalibratedAt = data.CalibratedAt,
            TraceId = data.TraceId,
        };
    }

    /// <summary>Debug 失敗時附帶的量測明細（含不合格球）。</summary>
    private static InstrumentCalibrationData BuildDiagnosticData(
        InstrumentCalibrationRequest request,
        string traceId,
        IReadOnlyList<MeasuredBall> allBalls,
        InstrumentCalibrationComputeResult computed,
        DatumPlaneMeasureResult? datumPlane)
    {
        return new()
        {
            ScannerId = request.ScannerId,
            TraceId = traceId,
            Compensation = computed.Compensation,
            ActualScannerPosition = computed.ActualScannerPosition,
            RmseXY = computed.Rmse,
            ScannerToFloorZ = computed.ScannerToFloorZ,
            MatchedBalls = computed.MatchedBalls,
            DatumPlane = MapDatumPlane(datumPlane),
            Balls = allBalls.Select(b => new MeasuredBallDto
            {
                Index = b.Index,
                X = b.X,
                Y = b.Y,
                Z = b.Z,
                Diameter = b.Diameter,
                Ok = b.IsWithinTolerance,
            }).ToList(),
        };
    }

    /// <inheritdoc />
    public Task<ApiResponse<RollerCalibrationData>> CalibrateRollerAsync(
        RollerCalibrationRequest request,
        CancellationToken ct = default)
    {
        _ = request;
        _ = ct;
        return Task.FromResult(SpecNotImplemented.Response<RollerCalibrationData>("POST /api/calibration/roller"));
    }

    /// <summary>依台船傳入 standardBalls 數量設定 SDK PatternMatch 偵測上限。</summary>
    private static LjCalibrationParameters WithDetectCount(LjCalibrationParameters source, int ballCount)
    {
        return new()
        {
            ExpectedBallDiameter = source.ExpectedBallDiameter,
            DiameterTolerance = source.DiameterTolerance,
            BallCount = ballCount,
            MaxDiameterError = source.MaxDiameterError,
            DatumPlane = source.DatumPlane,
        };
    }
}
