using System.Net;
using System.Threading;
using Keyence.LjDevCommon;
using Keyence.LjDevCommunication;
using Keyence.LjDevMeasure;
using Microsoft.Extensions.Options;
using ThreeDScan.Domain.Abstractions;
using ThreeDScan.Domain.Models;
using ThreeDScan.Infrastructure.Configuration;

namespace ThreeDScan.Infrastructure.Scanners;

/// <summary>LJScanWinform Form1.CaptureFromScanner + EnsureActiveScannerProgram.</summary>
public sealed class Ljs8SurfaceScannerCapture : ISurfaceScannerCapture
{
    private readonly ScannerEndpointOptions _endpoint;

    public Ljs8SurfaceScannerCapture(string scannerId, ScannerEndpointOptions endpoint)
    {
        ScannerId = scannerId;
        _endpoint = endpoint;
    }

    /// <inheritdoc />
    public string ScannerId { get; }

    /// <inheritdoc />
    public Task<ScannerCaptureResult> CaptureAsync(byte programNo, CancellationToken ct = default)
        => Task.Run(() => CaptureCore(programNo), ct);

    private ScannerCaptureResult CaptureCore(byte programNo)
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
                var state = Ljs8If.GetCurrentImageReceivingState(0);
                if (state._numberOfImage > bkNo) break;
                Thread.Sleep(10);
            }

            Ljs8If.StopImageReceiving(0);
        }
        finally
        {
            Ljs8If.CommunicationClose(0);
            // 不呼叫 Finalize：DLL 僅在程式結束時釋放，避免破壞全域連線狀態。
            Ljs8Runtime.ReleaseDevice();
        }

        var captureId = Guid.NewGuid().ToString("N");
        var captureDir = Path.Combine(Path.GetTempPath(), "ThreeDScan", ScannerId, captureId);
        Directory.CreateDirectory(captureDir);

        var heightPath = Path.Combine(captureDir, "capture_height.png");
        var grayPath = Path.Combine(captureDir, "capture_gray.png");
        var hErr = heightImage.Write(heightPath);
        var gErr = grayImage.Write(grayPath);
        if (hErr != LFileIOErrorCode.Success || gErr != LFileIOErrorCode.Success)
            throw new InvalidOperationException($"掃描影像寫檔失敗: height={hErr}, gray={gErr}");

        ScanCaptureCache.Store(captureId, heightImage, grayImage);

        return new ScannerCaptureResult
        {
            CaptureId = captureId,
            HeightImagePath = heightPath,
            GrayImagePath = grayPath,
            Geometry = new CaptureImageGeometry
            {
                Width = heightImage.XSize,
                Height = heightImage.YSize,
                OriginX = heightImage.ImagePosition.X,
                OriginY = heightImage.ImagePosition.Y,
                CoefficientX = heightImage.CoefficientX,
                CoefficientY = heightImage.CoefficientY,
            },
        };
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
