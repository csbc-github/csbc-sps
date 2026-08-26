using System.Threading;
using Keyence.LjDevCommon;
using Keyence.LjDevCommunication;
using Keyence.LjDevMeasure;
using ThreeDScan.Domain.Abstractions;
using ThreeDScan.Domain.Models;
using ThreeDScan.Infrastructure.Configuration;
using ThreeDScan.Infrastructure.Export;

namespace ThreeDScan.Infrastructure.Scanners;

/// <summary>
/// LJS8 擷取：校正走影像-only；UC-04 與 API 同走 C# Pipeline.Run。
/// </summary>
public sealed class Ljs8SurfaceScannerCapture : ISurfaceScannerCapture
{
    private readonly ScannerEndpointOptions _endpoint;
    private readonly string _exportRoot;
    private readonly ISurfaceScanPipeline _pipeline;

    public Ljs8SurfaceScannerCapture(
        string scannerId,
        ScannerEndpointOptions endpoint,
        string exportRoot,
        ISurfaceScanPipeline pipeline)
    {
        ScannerId = scannerId;
        _endpoint = endpoint;
        _exportRoot = exportRoot;
        _pipeline = pipeline;
    }

    /// <inheritdoc />
    public string ScannerId { get; }

    /// <inheritdoc />
    public Task<ScannerCaptureResult> CaptureAsync(byte programNo, CancellationToken ct = default)
    {
        return CaptureCoreAsync(programNo, processAngleSteel: false, ct);
    }

    /// <inheritdoc />
    public Task<ScannerCaptureResult> CaptureWithAngleSteelAsync(byte programNo, CancellationToken ct = default)
    {
        return CaptureCoreAsync(programNo, processAngleSteel: true, ct);
    }

    private async Task<ScannerCaptureResult> CaptureCoreAsync(
        byte programNo,
        bool processAngleSteel,
        CancellationToken ct)
    {
        var (heightImage, grayImage) = await Task.Run(() => CaptureFromScanner(programNo, ct), ct)
            .ConfigureAwait(false);

        var captureId = Guid.NewGuid().ToString("N");
        var captureDir = CaptureExportPath.CreateCaptureFolder(_exportRoot, ScannerId, captureId);

        var heightPath = Path.Combine(captureDir, "capture_height.png");
        var grayPath = Path.Combine(captureDir, "capture_gray.png");
        var hErr = heightImage.Write(heightPath);
        var gErr = grayImage.Write(grayPath);
        if (hErr != LFileIOErrorCode.Success || gErr != LFileIOErrorCode.Success)
            throw new InvalidOperationException($"掃描影像寫檔失敗: height={hErr}, gray={gErr}");

        ScanCaptureCache.Store(captureId, heightImage, grayImage);

        var geometry = new CaptureImageGeometry
        {
            Width = heightImage.XSize,
            Height = heightImage.YSize,
            OriginX = heightImage.ImagePosition.X,
            OriginY = heightImage.ImagePosition.Y,
            CoefficientX = heightImage.CoefficientX,
            CoefficientY = heightImage.CoefficientY,
        };

        var imageResult = new ScannerCaptureResult
        {
            CaptureId = captureId,
            ScannerId = ScannerId,
            HeightImagePath = heightPath,
            GrayImagePath = grayPath,
            Geometry = geometry,
        };

        if (!processAngleSteel)
        {
            return imageResult;
        }

        var processLogPath = Path.Combine(captureDir, "ply_processor.log");
        void Log(string msg)
        {
            DebugLog(msg);
            try
            {
                File.AppendAllText(processLogPath, $"{DateTime.Now:HH:mm:ss.fff} {msg}{Environment.NewLine}");
            }
            catch { }
        }

        Log("C# Pipeline.Run（raw PLY → 去地／去雜訊／ZIP／JSON／GLB／角度）...");
        var export = await _pipeline.ProcessCaptureAsync(imageResult, ct).ConfigureAwait(false);
        Log($"raw={Path.Combine(captureDir, "raw_scene.ply")}");
        Log($"PLY={export.PointCloudPath} ({export.PointCount} points)");
        if (export.AngleDegrees is { } deg)
        {
            Log($"角鋼夾角：{deg:F2}°（偏離 90° {export.AngleDeviationFromSquareDegrees:F2}°）");
        }
        else
        {
            Log("角鋼夾角：無法辨識兩個有效平面");
        }

        Log($"ZIP={export.ZipPath}");
        Log($"JSON={export.JsonPath}");
        if (!string.IsNullOrWhiteSpace(export.CompensatedJsonPath))
        {
            Log($"JSON compensated={export.CompensatedJsonPath}");
        }
        else
        {
            Log("JSON compensated：無該掃描器補償歷程，未另寫檔");
        }
        if (!string.IsNullOrWhiteSpace(export.GlbPath))
        {
            Log($"GLB={export.GlbPath}");
        }
        else
        {
            Log("GLB：網格未產出（稀疏物件時 Pipeline 可能略過）");
        }

        return new ScannerCaptureResult
        {
            CaptureId = captureId,
            ScannerId = ScannerId,
            HeightImagePath = heightPath,
            GrayImagePath = grayPath,
            PlyPath = export.PointCloudPath,
            PlyPointCount = export.PointCount,
            ZipPath = export.ZipPath,
            GlbPath = export.GlbPath,
            JsonPath = export.JsonPath,
            CompensatedJsonPath = export.CompensatedJsonPath,
            AngleDegrees = export.AngleDegrees,
            AngleDeviationFromSquareDegrees = export.AngleDeviationFromSquareDegrees,
            Geometry = geometry,
        };
    }

    private (LHeightImage Height, LGrayImage Gray) CaptureFromScanner(byte programNo, CancellationToken ct)
    {
        var ipParts = _endpoint.Ip.Split('.');
        if (ipParts.Length != 4)
            throw new InvalidOperationException($"Scanner {ScannerId} IP 格式錯誤: {_endpoint.Ip}");

        var ipAddress = new byte[]
        {
            byte.Parse(ipParts[0]),
            byte.Parse(ipParts[1]),
            byte.Parse(ipParts[2]),
            byte.Parse(ipParts[3]),
        };

        LHeightImage heightImage = new();
        LGrayImage grayImage = new();

        Ljs8Runtime.EnsureInitialized();
        Ljs8Runtime.AcquireDevice();
        var openRc = Ljs8If.CommunicationOpen(0, ipAddress, _endpoint.Port);
        if (openRc != 0)
        {
            Ljs8Runtime.ReleaseDevice();
            throw new InvalidOperationException($"CommunicationOpen 錯誤碼 {openRc}");
        }

        try
        {
            EnsureActiveScannerProgram(programNo);
            SyncRunningFromSavedProgram();

            Ljs8If.StartImageReceiving(0, _endpoint.HighSpeedPort, OnReceive, 0u);

            void OnReceive(LHeightImage h, LGrayImage g, Ljs8IfReceivedStatus _)
            {
                heightImage = h;
                grayImage = g;
            }

            Ljs8If.StartMeasure(0);

            var bkNo = Ljs8If.GetCurrentImageReceivingState(0)._numberOfImage;
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                var state = Ljs8If.GetCurrentImageReceivingState(0);
                if (state._numberOfImage > bkNo) break;
                Thread.Sleep(10);
            }

            Ljs8If.StopImageReceiving(0);
        }
        finally
        {
            Ljs8If.CommunicationClose(0);
            Ljs8Runtime.ReleaseDevice();
        }

        return (heightImage, grayImage);
    }

    private static void DebugLog(string msg)
    {
        System.Diagnostics.Debug.WriteLine("[ply_processor] " + msg);
    }

    private void EnsureActiveScannerProgram(byte requested)
    {
        byte active = 0;
        var rc = Ljs8If.GetActiveProgram(0, ref active);
        if (rc != 0)
            throw new InvalidOperationException($"GetActiveProgram 錯誤碼 {rc}");

        var changeRc = Ljs8If.ChangeActiveProgram(0, requested);
        Thread.Sleep(200);

        rc = Ljs8If.GetActiveProgram(0, ref active);
        if (changeRc != 0)
            throw new InvalidOperationException($"ChangeActiveProgram 錯誤碼 {changeRc}");

        if (active != requested)
            throw new InvalidOperationException(
                $"掃描器 {ScannerId} 未切到程式 {requested}，目前為 {active}");
    }

    private static void SyncRunningFromSavedProgram()
    {
        var rewriteRc = Ljs8If.RewriteTemporarySetting(0, Ljs8ProgramConstants.SettingDepthSave);
        if (rewriteRc != 0)
            throw new InvalidOperationException($"RewriteTemporarySetting(Save) 錯誤碼 {rewriteRc}");

        uint error = 0;
        var reflectRc = Ljs8If.ReflectSetting(0, Ljs8ProgramConstants.SettingDepthRunning, ref error);
        if (reflectRc != 0)
            throw new InvalidOperationException($"ReflectSetting(Running) 錯誤碼 {reflectRc}（請確認掃描器未在量測中）");
        if (error != 0)
            throw new InvalidOperationException($"ReflectSetting(Running) 詳細錯誤 0x{error:X8}");
    }
}
