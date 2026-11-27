using Keyence.LjDevCommon;
using Keyence.LjDevMeasure;
using ThreeDScan.Domain.Abstractions;
using ThreeDScan.Domain.Models;
using ThreeDScan.Infrastructure.Scanners;

namespace ThreeDScan.Infrastructure.Measure;

/// <summary>LJ Developer SDK 尋球與 Ref 建立實作。</summary>
public sealed class LjDeveloperMeasureService : ILjSphereMeasureService
{
    /// <inheritdoc />
    public Task<SphereMeasureResult> MeasureAsync(
        string scannerId,
        ScannerConfigRecord config,
        ScannerCaptureResult capture,
        LjCalibrationParameters parameters,
        CancellationToken ct = default)
    {
        return Task.Run(() => MeasureCore(scannerId, config, capture, parameters, ct), ct);
    }

    /// <inheritdoc />
    /// <remarks>實際邏輯在 <see cref="SetupReferenceCore"/>：指定座標擬合 → 重訓樣板 → 覆蓋 png。</remarks>
    public Task<ReferenceSetupResult> SetupReferenceAsync(
        string scannerId,
        ScannerConfigRecord? existingConfig,
        ScannerCaptureResult capture,
        LjCalibrationParameters parameters,
        string contentsFolder,
        CancellationToken ct = default)
    {
        return Task.Run(() => SetupReferenceCore(scannerId, existingConfig, capture, parameters, contentsFolder, ct), ct);
    }

    /// <summary>儀器校正量測：載入 Ref 圖、Execute 尋球、抽出球心、可選量基準面。</summary>
    private static SphereMeasureResult MeasureCore(
        string scannerId,
        ScannerConfigRecord config,
        ScannerCaptureResult capture,
        LjCalibrationParameters parameters,
        CancellationToken ct)
    {
        var settings = new LjMeasureSettings(parameters, config);
        BallSettings.SetScope(settings);
        try
        {
            Directory.CreateDirectory(config.ContentsFolder);
            EnsureRefImagesExist(config.ContentsFolder);

            var measure = CreateMeasure(config.ContentsFolder);
            var (height, gray) = LoadCapture(capture);

            // SDK 工具保留給視埠；API 球心／球徑必須讀取原始高度穩健擬合的保留值。
            var results = measure.Execute(height, gray, ct);
            var balls = ExtractBalls(measure, results, parameters).ToList();

            MeasuredBall? firstBall = balls.FirstOrDefault();
            var datumPlane = measure.TryMeasureDatumPlane(
                height,
                firstBall?.X,
                firstBall?.Y,
                firstBall?.Z);

            return new SphereMeasureResult
            {
                ScannerId = scannerId,
                Balls = balls,
                KeptCount = balls.Count,
                DatumPlane = datumPlane.Measured ? datumPlane : null,
            };
        }
        finally
        {
            BallSettings.ClearScope();
        }
    }

    /// <summary>
    /// Ref 建立核心：在 existingConfig.Ref* 指定座標擬合。
    /// 成功後 ActiveRef* 改為該次擬合球心，呼叫端寫回 DB；失敗則不覆蓋 png。
    /// </summary>
    private static ReferenceSetupResult SetupReferenceCore(
        string scannerId,
        ScannerConfigRecord? existingConfig,
        ScannerCaptureResult capture,
        LjCalibrationParameters parameters,
        string contentsFolder,
        CancellationToken ct)
    {
        Directory.CreateDirectory(contentsFolder);

        if (existingConfig is null)
        {
            throw new InvalidOperationException(
                $"找不到 ScannerConfig（{scannerId}）。" +
                "請先以 SQL 寫入種子 Ref 座標（database/seed-scannerconfig.sql），再執行 reference-setup。");
        }

        var settings = new LjMeasureSettings(parameters, existingConfig);
        BallSettings.SetScope(settings);
        try
        {
            var (height, gray) = LoadCapture(capture);

            // 對齊 Winform EnsureContentsFolderReady：首次無 ref 時先建立 ref_*.png
            var heightRefPath = Path.Combine(contentsFolder, "ref_height.png");
            if (!File.Exists(heightRefPath))
                PersistRefImages(contentsFolder, height, gray);

            var measure = CreateMeasure(contentsFolder);

            measure.ReloadReferenceFromConfig();

            if (!measure.TryFitCalibrationBallAtRef(
                    height, out var discoverX, out var discoverY, out var discoverD, out var discoverMessage, ct))
            {
                throw new InvalidOperationException(
                    "Ref 擬合失敗：" + discoverMessage + " 已取消覆蓋 ref_*.png。");
            }

            measure.SetReferenceCenters(discoverX, discoverY, discoverX, discoverY);
            measure.UpdatePattern(height, gray);
            measure.UpdateReference(height, gray);

            // 以相同原始球面擬合再驗證；直徑或外圈品質不合格則不覆蓋 png。
            if (!measure.TryValidateCalibrationProbe(
                    height, out _, out _, out _, out _, out var validateMessage, ct))
            {
                throw new InvalidOperationException(validateMessage + " 已取消覆蓋 ref_*.png。");
            }

            // 樣板／Ref 以該點擬合後的 UpdatePattern 為準。

            // 對齊 Winform PersistCalibration：舊 png 進 backup 後寫入本次高度／輝度當新 Ref 圖。
            PersistRefImages(contentsFolder, height, gray);

            return new ReferenceSetupResult
            {
                ScannerId = scannerId,
                RefPatternX = measure.ActiveRefPatternX,
                RefPatternY = measure.ActiveRefPatternY,
                RefSphereX = measure.ActiveRefSphereX,
                RefSphereY = measure.ActiveRefSphereY,
                DiscoveredDiameter = discoverD,
                Message = discoverMessage,
                ContentsFolder = contentsFolder,
            };
        }
        finally
        {
            BallSettings.ClearScope();
        }
    }

    /// <summary>建構量測核心前確認 ref_height.png 存在（樣板從這張圖訓）。</summary>
    private static LjDeveloperMeasureCore CreateMeasure(string contentsFolder)
    {
        EnsureRefImagesExist(contentsFolder);
        return new LjDeveloperMeasureCore(contentsFolder);
    }

    /// <summary>沒有 Ref 圖就不能量；請先跑 reference-setup。</summary>
    private static void EnsureRefImagesExist(string contentsFolder)
    {
        var heightPath = Path.Combine(contentsFolder, "ref_height.png");
        if (File.Exists(heightPath)) return;
        throw new InvalidOperationException(
            $"找不到參考影像 {heightPath}。請先執行 POST /api/calibration/reference-setup。");
    }

    /// <summary>舊 ref png 複製到 backup/ 後寫入本次影像。尋球失敗不應走到這裡。</summary>
    private static void PersistRefImages(string contentsFolder, LHeightImage height, LGrayImage gray)
    {
        var backupDir = Path.Combine(contentsFolder, "backup");
        Directory.CreateDirectory(backupDir);
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var heightPath = Path.Combine(contentsFolder, "ref_height.png");
        var grayPath = Path.Combine(contentsFolder, "ref_gray.png");

        if (File.Exists(heightPath))
            File.Copy(heightPath, Path.Combine(backupDir, $"ref_height_{stamp}.png"), true);
        if (File.Exists(grayPath))
            File.Copy(grayPath, Path.Combine(backupDir, $"ref_gray_{stamp}.png"), true);

        var hErr = height.Write(heightPath);
        var gErr = gray.Write(grayPath);
        if (hErr != LFileIOErrorCode.Success || gErr != LFileIOErrorCode.Success)
            throw new InvalidOperationException($"寫入 ref 影像失敗: height={hErr}, gray={gErr}");
    }

    /// <summary>優先用擷取快取的高度／輝度；沒快取才從檔案讀。</summary>
    private static (LHeightImage Height, LGrayImage Gray) LoadCapture(ScannerCaptureResult capture)
    {
        if (ScanCaptureCache.TryGet(capture.CaptureId, out var h, out var g))
            return (h, g);

        var height = new LHeightImage();
        var gray = new LGrayImage();
        if (capture.HeightImagePath is not null)
            height.Read(capture.HeightImagePath);
        if (capture.GrayImagePath is not null)
            gray.Read(capture.GrayImagePath);
        return (height, gray);
    }

    /// <summary>從 Execute 讀取有校正資格的球心，並保留自由估徑及模型標記。</summary>
    private static IEnumerable<MeasuredBall> ExtractBalls(
        LjDeveloperMeasureCore measure,
        IList<ILToolInfo> results,
        LjCalibrationParameters parameters)
    {
        var index = 0;
        foreach (var result in results)
        {
            if (!measure.TryReadKeptSphereFit(result, out var fit)) continue;

            yield return new MeasuredBall
            {
                Index = index++,
                X = fit.X,
                Y = fit.Y,
                Z = fit.Z,
                Diameter = fit.DiameterMm,
                IsWithinTolerance = Math.Abs(fit.DiameterMm - parameters.ExpectedBallDiameter) <= parameters.DiameterTolerance,
                KnownRadiusModelValidated = fit.Success && fit.UsesKnownRadius,
                SphereFitMethod = fit.UsesKnownRadius ? "KnownRadiusCenter" : "FreeRadiusRobust",
                UsesKnownRadius = fit.UsesKnownRadius,
                NominalDiameterMm = fit.UsesKnownRadius ? fit.NominalDiameterMm : parameters.ExpectedBallDiameter,
                FreeMeasurementQualityPassed = fit.IsGeometryValid,
                UsedLocalizedOutlierValidation = fit.UsedLocalizedOutlierValidation,
                CenterSensitivityMm = fit.UsedLocalizedOutlierValidation ? fit.CenterSensitivityMm : null,
            };
        }
    }
}
