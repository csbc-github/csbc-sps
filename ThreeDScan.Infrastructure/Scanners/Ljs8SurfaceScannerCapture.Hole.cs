using System.Text.Json;
using ThreeDScan.Domain.Models;
using ThreeDScan.Infrastructure.Export;
using ThreeDScan.Infrastructure.Export.Models;

namespace ThreeDScan.Infrastructure.Scanners;

public sealed partial class Ljs8SurfaceScannerCapture
{
    /// <summary>孔洞專用原始擷取；遵循提供的 OneClick 影像協定及區域格網。</summary>
    public Task<HoleRawCaptureResult> CaptureHoleAsync(byte? programNo = null, CancellationToken ct = default)
        => Task.Run(() => CaptureHole(programNo, ct), ct);

    private HoleRawCaptureResult CaptureHole(byte? programNo, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var captureId = Guid.NewGuid().ToString("N");
        var directory = CaptureExportPath.CreateCaptureFolder(_exportRoot, ScannerId, captureId);
        var logPath = Path.Combine(directory, "capture-hole.log");
        void Log(string message)
        {
            try { File.AppendAllText(logPath, $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}"); }
            catch (IOException) { }
        }

        bool replay = !string.IsNullOrWhiteSpace(_endpoint.OfflineCaptureDirectory);
        HeightFrame frame;
        if (replay)
        {
            var (height, gray) = LoadOfflineCaptureImages(_endpoint.OfflineCaptureDirectory);
            frame = LimitOfflineHoleFrame(HeightImageFrameAdapter.FromLHeightImage(height, gray));
            Log("Offline height image; OneClick first-1000-profile limit, no hardware trigger.");
        }
        else
        {
            Ljs8Runtime.AcquireDevice(ct);
            try
            {
                frame = new HoleCaptureAcquisition(new HoleCaptureNativeSdk()).Capture(
                    new HoleCaptureOptions(_endpoint.Ip, _endpoint.Port, programNo), ct, Log);
            }
            finally { Ljs8Runtime.ReleaseDevice(); }
        }

        ct.ThrowIfCancellationRequested();
        var path = Path.Combine(directory, "raw.ply");
        HeightFramePlyExporter.SavePly(frame, path, PointCloudOrigin.LocalGrid, ct);
        var metadata = new SegmentCaptureMetadata
        {
            Width = frame.Info.XPointCount,
            ProfileCount = frame.ProfileCount,
            PitchXmm = frame.Info.PitchX / 100000d,
            PitchYmm = frame.Info.PitchY / 100000d,
            PitchZmm = frame.Info.PitchZ / 100000d,
            ScannerXStartMm = frame.Info.XStart / 100000d,
            ScannerYStartMm = frame.Info.YStart / 100000d,
            CoordinateOrigin = "local-grid",
            CoordinateSystem = "scanner",
            Unit = "mm",
        };
        File.WriteAllText(Path.Combine(directory, "capture.json"), JsonSerializer.Serialize(new
        {
            protocol = "OneClick.GetHeightImageSimpleArray",
            requestedProfileLimit = HoleCaptureAcquisition.RequestedProfiles,
            frame.ImageNo, frame.FirstProfileNo, metadata, isReplay = replay,
        }));
        return new HoleRawCaptureResult { CaptureId = captureId, RawPlyPath = path, Metadata = metadata, IsReplay = replay };
    }

    private static HeightFrame LimitOfflineHoleFrame(HeightFrame source)
    {
        HeightFramePlyExporter.Validate(source);
        int count = Math.Min(HoleCaptureAcquisition.RequestedProfiles, source.ProfileCount);
        if (count == source.ProfileCount) return source;
        int length = checked(source.Info.XPointCount * count);
        return new HeightFrame { Info = source.Info, ImageNo = source.ImageNo, FirstProfileNo = source.FirstProfileNo,
            ProfileCount = count, Heights = source.Heights[..length], Luminance = source.Luminance?[..length] };
    }
}
