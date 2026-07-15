using Microsoft.Extensions.Options;
using ThreeDScan.Application.Calibration.Dtos;
using ThreeDScan.Domain.Abstractions;
using ThreeDScan.Domain.Models;

namespace ThreeDScan.Application.Calibration;

/// <summary>校正應用服務：UC-01 儀器校正、Ref 建立與 WPF 預覽推送。</summary>
public sealed class CalibrationAppService : ICalibrationAppService
{
    private readonly IScannerRegistry _registry;
    private readonly IScannerConfigStore _configStore;
    private readonly ICompensationStore _compensationStore;
    private readonly IOperationLogStore _operationLogStore;
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
        IScannerConfigStore configStore,
        ICompensationStore compensationStore,
        IOperationLogStore operationLogStore,
        IScannerCaptureFactory captureFactory,
        ILjSphereMeasureEngine measureEngine,
        ICalibrationPreviewPublisher previewPublisher,
        IOptions<LjCalibrationParameters> calibrationParams,
        IOptions<CalibrationSettings> calibrationSettings,
        IStorageSettings storage,
        InstrumentCalibrationService? instrumentCalibration = null)
    {
        _registry = registry;
        _configStore = configStore;
        _compensationStore = compensationStore;
        _operationLogStore = operationLogStore;
        _captureFactory = captureFactory;
        _measureEngine = measureEngine;
        _previewPublisher = previewPublisher;
        _calibrationParams = calibrationParams.Value;
        _calibrationSettings = calibrationSettings.Value;
        _storage = storage;
        _instrumentCalibration = instrumentCalibration ?? new InstrumentCalibrationService();
    }

    /// <inheritdoc />
    public async Task<ApiResponse<InstrumentCalibrationData>> CalibrateInstrumentAsync(
        InstrumentCalibrationRequest request,
        CancellationToken ct = default)
    {
        // 本次校正追蹤 ID；失敗時 catch 區塊亦需推送預覽，故預先宣告可為 null 的掃描產物。
        var traceId = Guid.NewGuid().ToString("N");
        ScannerCaptureResult? capture = null;
        ScannerConfigRecord? config = null;
        string? previewGrayPath = null;
        string? previewHeightPath = null;
        byte programNo = 0;

        try
        {
            // ── 1. 掃描器與設定檢查 ──────────────────────────────────────────
            // 確認 ScannerId 已註冊，並解析本次使用的 Navigator Scan 群組編號。
            if (!_registry.TryGetEndpoint(request.ScannerId, out var endpoint))
                return await InstrumentFailAsync(request, traceId, "CAL_CFG_001", $"未知 ScannerId: {request.ScannerId}", null, ct);

            programNo = _registry.ResolveProgramNo(request.ScannerId, request.NavigatorScanNo ?? endpoint.NavigatorScanNo);

            // Ref 建立後寫入的 ScannerConfig（含 Ref 座標、球徑等）；無資料則無法量測。
            config = await _configStore.GetAsync(request.ScannerId, ct);
            if (config is null)
                return await InstrumentFailAsync(request, traceId, "CAL_CFG_003",
                    $"找不到 ScannerConfig（{request.ScannerId}）。請先執行 seed-scannerconfig.sql，再執行 Ref 建立 API。", null, ct);

            // ── 2. 觸發掃描並儲存預覽影像 ────────────────────────────────────
            // 向 LJ SDK 取一幀灰階／高度圖；複製至 ContentsRoot 供靜態檔與 WPF 3D 監看。
            capture = await _captureFactory.GetCapture(request.ScannerId).CaptureAsync(programNo, ct);
            var previewPaths = SavePreviewImages(capture, request.ScannerId, programNo, traceId);
            previewGrayPath = previewPaths.GrayRelativePath;
            previewHeightPath = previewPaths.HeightRelativePath;

            // ── 3. 組合量測參數並執行 SDK 尋球 ───────────────────────────────
            // 合併 appsettings 與 ScannerConfig 的球徑／基準面設定；
            // DetectCount 以台船傳入的 standardBalls 數量為準（1～10），與 Ref 建立的 BallCount 無關。
            var effectiveParams = LjCalibrationMerge.ForScanner(config, _calibrationParams);
            var detectCount = Math.Clamp(request.StandardBalls.Count, 1, 10);
            var measureParams = WithDetectCount(effectiveParams, detectCount);

            var measure = await _measureEngine.MeasureAsync(request.ScannerId, config, capture, measureParams, ct);

            // KeptCount = 直徑在容許範圍內且通過 SDK 篩選的球數；0 顆表示完全無法辨識標準球。
            if (measure.KeptCount == 0)
            {
                var feedMsg =
                    $"標準球未偵測到或無法識別（需 {detectCount} 顆，量得 0 顆；" +
                    $"ExpectedBallDiameter={measureParams.ExpectedBallDiameter:F1} mm）。";
                await PublishPreviewAsync(
                    request.ScannerId, traceId, false, measure.Balls, config, capture,
                    previewGrayPath, previewHeightPath,
                    null, null, null, null, feedMsg, ct,
                    measureParams, detectCount);
                return await InstrumentFailAsync(request, traceId, "CAL_FEED_001", feedMsg, null, ct);
            }

            // ── 4. 領域計算：已知座標 ↔ 量測球心配對，反推補償 offset ─────────
            // 優先使用直徑合格球配對；數量不足時退回全部量測球（由 Compute 再檢查球數）。
            var pairingBalls = SelectBallsForPairing(measure.Balls, request.StandardBalls.Count);

            InstrumentCalibrationComputeResult computed;
            try
            {
                computed = _instrumentCalibration.Compute(
                    request.CameraExpectedPosition,  // 台船宣告的掃描器預期絕對位置
                    request.StandardBalls,             // 標準球在機台座標系的已知 (X,Y,Z)
                    pairingBalls);                     // SDK 量得的球心（掃描器座標系）
            }
            catch (InstrumentCalibrationException ex)
            {
                // 球數不足或 1:1 配對失敗（CAL_FEED_001 等）
                await PublishPreviewAsync(
                    request.ScannerId, traceId, false, measure.Balls, config, capture,
                    previewGrayPath, previewHeightPath,
                    null, null, null, null, ex.Message, ct);
                return await InstrumentFailAsync(request, traceId, ex.ErrorCode, ex.Message, null, ct);
            }

            // ── 5. RMSE 品質門檻 ─────────────────────────────────────────────
            // RMSE 為配對後標準球間「相對距離」誤差；≥ 1.0 mm 視為量測不可靠，不寫入補償。
            if (computed.Rmse >= InstrumentCalibrationService.RmseThresholdMm)
            {
                var rmseMsg = InstrumentCalibrationDiagnostics.FormatRmseFailure(computed);
                await PublishPreviewAsync(
                    request.ScannerId, traceId, false, measure.Balls, config, capture,
                    previewGrayPath, previewHeightPath,
                    computed.Compensation.OffsetX, computed.Compensation.OffsetY, computed.Compensation.OffsetZ,
                    computed.Rmse, rmseMsg, ct,
                    measureParams, detectCount, computed.MatchedBalls);
                return await InstrumentFailAsync(
                    request, traceId, "CAL_FEED_003", rmseMsg,
                    BuildDiagnosticData(request, traceId, measure.Balls, computed, measure.DatumPlane), ct);
            }

            // ── 6. 寫入補償歷程 ──────────────────────────────────────────────
            // offset = 實際掃描器位置 − 台船預期位置；後續量測套用此補償修正座標。
            var offset = new CompensationOffset
            {
                ScannerId = request.ScannerId,
                OffsetX = computed.Compensation.OffsetX,
                OffsetY = computed.Compensation.OffsetY,
                OffsetZ = computed.Compensation.OffsetZ,
                Rmse = computed.Rmse,
                MachineArea = request.MachineArea,
                CalibratedAt = DateTimeOffset.Now,
                Operator = request.Operator,
                TraceId = traceId,
            };
            await _compensationStore.AppendAsync(offset, ct);

            // ── 7. 組裝回應並推送成功預覽 ────────────────────────────────────
            var data = new InstrumentCalibrationData
            {
                ScannerId = request.ScannerId,
                TraceId = traceId,
                Compensation = computed.Compensation,
                ActualScannerPosition = computed.ActualScannerPosition,
                CalibratedAt = offset.CalibratedAt.ToString("O"),
                // API 回傳僅含直徑合格球；失敗診斷時由 BuildDiagnosticData 回傳全部球。
                Balls = measure.Balls
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
                DatumPlane = MapDatumPlane(measure.DatumPlane),
                ScannerToFloorZ = computed.ScannerToFloorZ,
                Rmse = computed.Rmse,
            };

            await PublishPreviewAsync(
                request.ScannerId, traceId, true, measure.Balls, config, capture,
                previewGrayPath, previewHeightPath,
                offset.OffsetX, offset.OffsetY, offset.OffsetZ, computed.Rmse, null, ct,
                measureParams, detectCount);
            return await InstrumentOkAsync(request, traceId, data, ct);
        }
        catch (InstrumentCalibrationException ex)
        {
            // 掃描前階段拋出的領域例外（理論上多數已在 try 內攔截）
            await PublishPreviewAsync(
                request.ScannerId, traceId, false, Array.Empty<MeasuredBall>(), config, capture,
                previewGrayPath, previewHeightPath,
                null, null, null, null, ex.Message, ct);
            return await InstrumentFailAsync(request, traceId, ex.ErrorCode, ex.Message, null, ct);
        }
        catch (NotImplementedException ex)
        {
            // SDK 尚未實作（開發／模擬環境）
            await PublishPreviewAsync(
                request.ScannerId, traceId, false, Array.Empty<MeasuredBall>(), config, capture,
                previewGrayPath, previewHeightPath,
                null, null, null, null, ex.Message, ct);
            return await InstrumentFailAsync(request, traceId, "CAL_SDK_001", ex.Message, null, ct);
        }
        catch (Exception ex)
        {
            // 未預期錯誤（硬體、IO、資料庫等）
            await PublishPreviewAsync(
                request.ScannerId, traceId, false, Array.Empty<MeasuredBall>(), config, capture,
                previewGrayPath, previewHeightPath,
                null, null, null, null, ex.Message, ct);
            return await InstrumentFailAsync(request, traceId, "CAL_SYS_001", ex.Message, null, ct);
        }
    }

    /// <inheritdoc />
    public async Task<ApiResponse<ReferenceSetupData>> SetupReferenceAsync(
        ReferenceSetupRequest request,
        CancellationToken ct = default)
    {
        var traceId = Guid.NewGuid().ToString("N");
        try
        {
            if (!_registry.TryGetEndpoint(request.ScannerId, out var endpoint))
                return await ReferenceFailAsync(request, traceId, "CAL_CFG_001", $"未知 ScannerId: {request.ScannerId}", ct);

            var programNo = _registry.ResolveProgramNo(request.ScannerId, request.NavigatorScanNo ?? endpoint.NavigatorScanNo);
            var contentsFolder = _registry.ResolveScannerContentsFolder(request.ScannerId, _storage.ContentsRoot);
            var existing = await _configStore.GetAsync(request.ScannerId, ct);
            var effectiveParams = LjCalibrationMerge.ForScanner(existing, _calibrationParams);

            var capture = await _captureFactory.GetCapture(request.ScannerId).CaptureAsync(programNo, ct);
            var setup = await _measureEngine.SetupReferenceAsync(
                request.ScannerId, existing, capture, effectiveParams, contentsFolder, ct);

            var record = new ScannerConfigRecord
            {
                ScannerId = request.ScannerId,
                RefPatternX = setup.RefPatternX,
                RefPatternY = setup.RefPatternY,
                RefSphereX = setup.RefSphereX,
                RefSphereY = setup.RefSphereY,
                ContentsFolder = setup.ContentsFolder,
                UpdatedAt = DateTimeOffset.Now,
                UpdatedBy = request.Operator,
                TraceId = traceId,
                ExpectedBallDiameter = setup.DiscoveredDiameter,
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
            await _configStore.UpsertAsync(record, ct);

            await LogOperationAsync(
                traceId, request.Operator, "SetupReference", request.ScannerId,
                success: true, errorCode: null, detail: setup.Message, ct);

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

    private async Task<ApiResponse<InstrumentCalibrationData>> InstrumentOkAsync(
        InstrumentCalibrationRequest request,
        string traceId,
        InstrumentCalibrationData data,
        CancellationToken ct)
    {
        await LogOperationAsync(
            traceId, request.Operator, "CalibrateInstrument", request.ScannerId,
            success: true, errorCode: null,
            detail: $"offset=({data.Compensation.OffsetX:F3},{data.Compensation.OffsetY:F3},{data.Compensation.OffsetZ:F3}) rmse={data.Rmse:F3}",
            ct);
        return ApiResponse<InstrumentCalibrationData>.Ok(ApplyResponseMode(data));
    }

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

    private Task LogOperationAsync(
        string traceId,
        string? operatorName,
        string action,
        string target,
        bool success,
        string? errorCode,
        string? detail,
        CancellationToken ct) =>
        _operationLogStore.AppendAsync(new OperationLogEntry
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

    private InstrumentCalibrationData ApplyResponseMode(InstrumentCalibrationData data)
    {
        if (_calibrationSettings.IsDebugResponse)
            return data;

        return new InstrumentCalibrationData
        {
            ScannerId = data.ScannerId,
            TraceId = data.TraceId,
            Compensation = data.Compensation,
            Rmse = data.Rmse,
            CalibratedAt = data.CalibratedAt,
        };
    }

    private static InstrumentCalibrationData BuildDiagnosticData(
        InstrumentCalibrationRequest request,
        string traceId,
        IReadOnlyList<MeasuredBall> allBalls,
        InstrumentCalibrationComputeResult computed,
        DatumPlaneMeasureResult? datumPlane) =>
        new()
        {
            ScannerId = request.ScannerId,
            TraceId = traceId,
            Compensation = computed.Compensation,
            ActualScannerPosition = computed.ActualScannerPosition,
            Rmse = computed.Rmse,
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

    /// <summary>依台船傳入 standardBalls 數量設定 SDK PatternMatch 偵測上限。</summary>
    private static LjCalibrationParameters WithDetectCount(LjCalibrationParameters source, int ballCount) =>
        new()
        {
            ExpectedBallDiameter = source.ExpectedBallDiameter,
            DiameterTolerance = source.DiameterTolerance,
            BallCount = ballCount,
            MaxDiameterError = source.MaxDiameterError,
            DatumPlane = source.DatumPlane,
        };
}
