using Keyence.LjDevCommon;
using Keyence.LjDevMeasure;
using ThreeDScan.Domain.Abstractions;
using ThreeDScan.Domain.Models;
using ThreeDScan.Infrastructure.LjSdk;
using ThreeDScan.Infrastructure.Scanners;

namespace ThreeDScan.Infrastructure.Measure;

/// <summary>LJ Developer SDK 尋球與 Ref 建立實作。</summary>
public sealed class LjDeveloperMeasureEngine : ILjSphereMeasureEngine
{
    /// <inheritdoc />
    public Task<SphereMeasureResult> MeasureAsync(
        string scannerId,
        ScannerConfigRecord config,
        ScannerCaptureResult capture,
        LjCalibrationParameters parameters,
        CancellationToken ct = default)
        => Task.Run(() => MeasureCore(scannerId, config, capture, parameters), ct);

    /// <inheritdoc />
    public Task<ReferenceSetupResult> SetupReferenceAsync(
        string scannerId,
        ScannerConfigRecord? existingConfig,
        ScannerCaptureResult capture,
        LjCalibrationParameters parameters,
        string contentsFolder,
        CancellationToken ct = default)
        => Task.Run(() => SetupReferenceCore(scannerId, existingConfig, capture, parameters, contentsFolder), ct);

    private static SphereMeasureResult MeasureCore(
        string scannerId,
        ScannerConfigRecord config,
        ScannerCaptureResult capture,
        LjCalibrationParameters parameters)
    {
        var settings = new LjMeasureSettings(parameters, config);
        BallSettings.SetScope(settings);
        try
        {
            Directory.CreateDirectory(config.ContentsFolder);
            EnsureRefImagesExist(config.ContentsFolder);

            var measure = CreateMeasure(config.ContentsFolder);
            var (height, gray) = LoadCapture(capture);

            var results = measure.Execute(height, gray);
            var balls = ExtractBalls(results, parameters).ToList();

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

    private static ReferenceSetupResult SetupReferenceCore(
        string scannerId,
        ScannerConfigRecord? existingConfig,
        ScannerCaptureResult capture,
        LjCalibrationParameters parameters,
        string contentsFolder)
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

            // 對齊 Winform CalibrateButton：先 ReloadReferenceFromConfig 再量測
            measure.ReloadReferenceFromConfig();

            if (!measure.TryDiscoverCalibrationBallCenter(
                    height, out var discoverX, out var discoverY, out var discoverD, out var discoverMessage))
            {
                throw new InvalidOperationException(
                    "校正尋球失敗：" + discoverMessage + " 已取消覆蓋 ref_*.png。");
            }

            measure.SetReferenceCenters(discoverX, discoverY, discoverX, discoverY);
            measure.UpdatePattern(height, gray);
            measure.UpdateReference(height, gray);

            if (!measure.TryValidateCalibrationProbe(
                    height, out _, out _, out _, out _, out var validateMessage))
            {
                throw new InvalidOperationException(validateMessage + " 已取消覆蓋 ref_*.png。");
            }

            var result = measure.Execute(height, gray);

            if (measure.TryGetFirstKeptSphere(
                    result, out var ballX, out var ballY, out _, out var ballD, out _))
            {
                measure.SetReferenceCenters(ballX, ballY, ballX, ballY);
                measure.UpdatePattern(height, gray);
                measure.UpdateReference(height, gray);

                if (!measure.TryValidateCalibrationProbe(
                        height, out _, out _, out _, out _, out validateMessage))
                {
                    throw new InvalidOperationException(
                        "以量測球心重訓後驗證失敗：" + validateMessage + " 已取消覆蓋 ref_*.png。");
                }

                result = measure.Execute(height, gray);
                discoverD = ballD;
            }

            // 對齊 Winform PersistReferenceCenters + PersistCalibration
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

    private static LjDeveloperMeasureCore CreateMeasure(string contentsFolder)
    {
        EnsureRefImagesExist(contentsFolder);
        return new LjDeveloperMeasureCore(contentsFolder);
    }

    private static void EnsureRefImagesExist(string contentsFolder)
    {
        var heightPath = Path.Combine(contentsFolder, "ref_height.png");
        if (File.Exists(heightPath)) return;
        throw new InvalidOperationException(
            $"找不到參考影像 {heightPath}。請先執行 POST /api/calibration/reference-setup。");
    }

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

    private static IEnumerable<MeasuredBall> ExtractBalls(
        IList<ILToolInfo> results,
        LjCalibrationParameters parameters)
    {
        var index = 0;
        foreach (var result in results)
        {
            if (result is not LSphereToolInfo sphereInfo) continue;
            var values = sphereInfo.ToolMeasureValues().Values;
            if (values is null || values.Length < 4) continue;

            var d = values[3].Value;
            if (!BallSettings.IsAcceptableSphereDiameter(d)) continue;

            yield return new MeasuredBall
            {
                Index = index++,
                X = values[0].Value,
                Y = values[1].Value,
                Z = values[2].Value,
                Diameter = d,
                IsWithinTolerance = Math.Abs(d - parameters.ExpectedBallDiameter) <= parameters.DiameterTolerance,
            };
        }
    }
}
