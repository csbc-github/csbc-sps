using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ThreeDScan.Infrastructure.Configuration;
using ThreeDScan.Infrastructure.LineScan.Hank;
using ThreeDScan.Infrastructure.LineScan.Models;

namespace ThreeDScan.Infrastructure.LineScan;

/// <summary>線掃描裝置宿主：端點鎖定、錄製與滾輪清空（對應 Hank SickScanApiBackend 核心）。</summary>
public sealed class LineScanDeviceHost
{
    readonly ConcurrentDictionary<string, Capture> _captures = new(StringComparer.Ordinal);
    readonly ConcurrentDictionary<string, SetupPreview> _setupPreviews = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, string> _endpoints = new(StringComparer.OrdinalIgnoreCase);
    readonly object _gate = new();
    readonly LineScanOptionsResolver _resolver;
    readonly LineScanEncoderSimulationPolicy _encoderSimulation;
    readonly ILogger<LineScanDeviceHost>? _logger;

    /// <summary>建立裝置宿主。</summary>
    public LineScanDeviceHost(LineScanOptionsResolver resolver,
        LineScanEncoderSimulationPolicy? encoderSimulation = null,
        ILogger<LineScanDeviceHost>? logger = null)
    {
        _resolver = resolver;
        _encoderSimulation = encoderSimulation ?? new(Options.Create(new LineScanEncoderSimulationOptions()));
        _logger = logger;
    }

    /// <summary>滾輪清空檢查並回傳 Z 距離。</summary>
    public async Task<double> CalibrateRollerAsync(string scannerId, CancellationToken ct = default)
    {
        await StopSetupPreviewAsync(scannerId).ConfigureAwait(false);
        var endpoint = _resolver.RequireEndpoint(scannerId);
        var calibration = await _resolver.GetCalibrationAsync(scannerId, ct).ConfigureAwait(false);
        var db = await _resolver.RequireDbSettingsAsync(scannerId, ct).ConfigureAwait(false);
        double tolerance = endpoint.RollerClearanceToleranceMm;
        if (!double.IsFinite(tolerance) || tolerance <= 0
            || !double.IsFinite(endpoint.Xmin) || !double.IsFinite(endpoint.Xmax) || endpoint.Xmin >= endpoint.Xmax)
        {
            throw new LineScanDeviceException("CAL_CFG_001", "滾輪清空容許值或 X 範圍設定無效。");
        }

        var roi = db.RoiBoundsOrNull();
        string taskId = "roller-" + Guid.NewGuid().ToString("N");
        string endpointKey = Claim(endpoint, taskId);
        try
        {
            await using var lease = await OpenAsync(endpoint, ct).ConfigureAwait(false);
            var scan = await lease.First.ConfigureAwait(false);
            var candidates = scan.Points
                .Where(point => point is P p && p.Finite && Inside(p, roi))
                .Select(point => point!.Value)
                .Where(point =>
                {
                    double x = calibration.Transform(point).X;
                    return x >= endpoint.Xmin && x <= endpoint.Xmax;
                })
                .ToList();
            if (candidates.Count < 8 || candidates.Any(point => calibration.Transform(point).Y > tolerance))
            {
                throw new LineScanDeviceException(
                    "CAL_LINE_004",
                    "滾輪尚未清空，或有效平面資料不足；請清空台面並確認框選範圍。");
            }

            var support = candidates
                .Where(point => Math.Abs(calibration.Transform(point).Y) <= tolerance)
                .OrderBy(point => calibration.Transform(point).X)
                .ToList();
            if (support.Count < 8 || support[0].Distance(support[^1]) < 10)
            {
                throw new LineScanDeviceException("CAL_LINE_004", "清空台面的可見支撐點不足，無法計算有效 Z 距離。");
            }

            var plane = Line.Fit(support);
            var referenceDirection = (calibration.Second - calibration.First)
                * (1 / calibration.First.Distance(calibration.Second));
            double zDistance = Math.Abs(plane.Direction.Cross(plane.Center));
            if (!double.IsFinite(zDistance) || zDistance <= 0 || plane.Rms > tolerance
                || Math.Abs(plane.Direction.Dot(referenceDirection)) < .99)
            {
                throw new LineScanDeviceException("CAL_LINE_004", "目前量測平面與桌面校正不一致，無法確認滾輪已清空。");
            }

            return zDistance;
        }
        finally
        {
            Release(endpointKey, taskId);
        }
    }

    /// <summary>開始線掃描錄製；taskId 為工作目錄資料夾名稱。</summary>
    public async Task StartLineAsync(
        string sessionDirectory,
        string scannerId,
        double rollerSpeed,
        string? traceId,
        CancellationToken ct = default)
    {
        await _resolver.ValidateForStartAsync(scannerId, ct).ConfigureAwait(false);
        await StopSetupPreviewAsync(scannerId).ConfigureAwait(false);
        var endpoint = _resolver.RequireEndpoint(scannerId);
        var calibration = await _resolver.GetCalibrationAsync(scannerId, ct).ConfigureAwait(false);
        var reconstruction = await _resolver.GetReconstructionOptionsAsync(scannerId, ct).ConfigureAwait(false);
        string taskId = Path.GetFileName(sessionDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(taskId))
        {
            throw new LineScanDeviceException("SCAN_LINE_CONFIG", "工作目錄無效。");
        }

        string endpointKey = Claim(endpoint, taskId);
        LineScanLease? lease = null;
        EncoderScanSession? session = null;
        try
        {
            lease = await OpenAsync(endpoint, ct).ConfigureAwait(false);
            var first = await lease.First.ConfigureAwait(false);
            if (!_encoderSimulation.Enabled && first.Encoders.Count < reconstruction.EncoderChannel)
            {
                throw new LineScanDeviceException("SCAN_ENCODER_UNAVAILABLE", "有效掃描封包未包含設定的 Encoder 通道。");
            }

            Directory.CreateDirectory(sessionDirectory);
            _logger?.LogInformation("Line scan {ScannerId}: Encoder mode {EncoderMode}, simulated forward speed {SpeedMmPerSec} mm/s.",
                scannerId, _encoderSimulation.Enabled ? "simulation" : "real", _encoderSimulation.ForwardSpeedMmPerSec);
            session = _encoderSimulation.StartSession(
                Path.Combine(sessionDirectory, "line_raw.ply"),
                calibration,
                reconstruction,
                new
                {
                    scannerId,
                    taskId,
                    traceId,
                    rollerSpeed,
                    api = "DAS-v1.1",
                    coordinateFrame = "calibrated_local_encoder",
                });
            var capture = new Capture(taskId, scannerId, endpointKey, calibration, reconstruction, lease, session);
            if (!_captures.TryAdd(taskId, capture))
            {
                throw new LineScanDeviceException("SCAN_BUSY", "此線掃描任務已開始。");
            }

            lease.Attach(session);
            lease.EnableLiveCache(endpoint.AverageScans);
            if (session.Recorder.Error is string error)
            {
                throw new LineScanDeviceException("SCAN_ENCODER_UNAVAILABLE", error);
            }

            ct.ThrowIfCancellationRequested();
        }
        catch
        {
            _captures.TryRemove(taskId, out _);
            if (lease != null)
            {
                await lease.DisposeAsync().ConfigureAwait(false);
            }

            if (session != null)
            {
                session.Recorder.Fail("線掃描啟動未完成");
                await session.Recorder.Stop().ConfigureAwait(false);
            }

            Release(endpointKey, taskId);
            throw;
        }
    }

    /// <summary>結束錄製並完成 3D 重建（不含量測）。</summary>
    public async Task FinalizeLineAsync(string taskId, CancellationToken ct = default)
    {
        if (!_captures.TryGetValue(taskId, out var capture))
        {
            throw new LineScanDeviceException("SCAN_LINE_001", "線掃描 taskId 不存在、已結束或已逾時。");
        }

        Task completion;
        lock (capture.Gate)
        {
            completion = capture.Completion ??= FinalizeCoreAsync(capture);
        }

        await completion.WaitAsync(ct).ConfigureAwait(false);
    }

    /// <summary>開始架設長連預覽（O8）；同一 scanner 重複呼叫為 no-op。</summary>
    public async Task StartSetupPreviewAsync(string scannerId, CancellationToken ct = default)
    {
        var endpoint = _resolver.RequireEndpoint(scannerId);
        if (endpoint.AverageScans is < 1 or > 600)
        {
            throw new LineScanDeviceException("SCAN_PROFILE_CONFIG", "Average scans must be between 1 and 600.");
        }
        if (_setupPreviews.TryGetValue(scannerId, out var existing))
        {
            existing.Lease.EnableLiveCache(endpoint.AverageScans);
            return;
        }
        string ownerId = "setup-" + scannerId.Trim();
        string endpointKey = Claim(endpoint, ownerId);
        LineScanLease? lease = null;
        try
        {
            lease = await OpenAsync(endpoint, ct).ConfigureAwait(false);
            lease.EnableLiveCache(endpoint.AverageScans);
            var setup = new SetupPreview(scannerId.Trim(), endpointKey, lease);
            if (!_setupPreviews.TryAdd(scannerId.Trim(), setup))
            {
                await lease.DisposeAsync().ConfigureAwait(false);
                Release(endpointKey, ownerId);
                return;
            }
        }
        catch
        {
            if (lease != null)
            {
                await lease.DisposeAsync().ConfigureAwait(false);
            }

            Release(endpointKey, ownerId);
            throw;
        }
    }

    /// <summary>結束架設預覽並釋放端點。</summary>
    public async Task StopSetupPreviewAsync(string scannerId)
    {
        if (!_setupPreviews.TryRemove(scannerId.Trim(), out var setup))
        {
            return;
        }

        await setup.Lease.DisposeAsync().ConfigureAwait(false);
        Release(setup.EndpointKey, "setup-" + setup.ScannerId);
    }

    /// <summary>架設預覽快取之最新一圈。</summary>
    public bool TryGetSetupLive(string scannerId, out double[] x, out double[] y)
    {
        if (_setupPreviews.TryGetValue(scannerId.Trim(), out var setup)
            && setup.Lease.TryGetLiveSnapshot(out x, out y))
        {
            return true;
        }

        x = [];
        y = [];
        return false;
    }

    /// <summary>錄製中任務的最新一圈。</summary>
    public bool TryGetRecordingLive(string taskId, out double[] x, out double[] y)
    {
        if (_captures.TryGetValue(taskId, out var capture)
            && capture.Lease.TryGetLiveSnapshot(out x, out y))
        {
            return true;
        }

        x = [];
        y = [];
        return false;
    }

    /// <summary>單次凍結輪廓（POST scan/profile）。</summary>
    public async Task<LineScanProfileCaptureResult> CaptureProfileAsync(string scannerId, CancellationToken ct = default)
    {
        await StopSetupPreviewAsync(scannerId).ConfigureAwait(false);
        var endpoint = _resolver.RequireEndpoint(scannerId);
        if (endpoint.AverageScans is < 1 or > 600)
        {
            throw new LineScanDeviceException("SCAN_PROFILE_CONFIG", "平均掃描圈數須為 1～600");
        }

        string ownerId = "profile-" + Guid.NewGuid().ToString("N");
        string endpointKey = Claim(endpoint, ownerId);
        try
        {
            await using var lease = await OpenAsync(endpoint, ct).ConfigureAwait(false);
            var profile = await lease.CaptureProfileAsync(
                endpoint.AverageScans,
                TimeSpan.FromSeconds(endpoint.ConnectionTimeoutSeconds),
                ct).ConfigureAwait(false);
            var xs = new List<double>();
            var ys = new List<double>();
            foreach (var value in profile.Points)
            {
                if (value is P point && point.Finite)
                {
                    xs.Add(point.X);
                    ys.Add(point.Y);
                }
            }

            if (xs.Count == 0)
            {
                throw new LineScanDeviceException("SCAN_PROFILE_EMPTY", "輪廓擷取後沒有有效點，請確認視野與掃描器資料");
            }

            var encoders = new List<LineScanEncoderSample>();
            foreach (var reading in profile.LastScan.Encoders)
            {
                encoders.Add(new LineScanEncoderSample
                {
                    Position = reading.Position,
                    Reserved = reading.Reserved,
                });
            }

            return new LineScanProfileCaptureResult
            {
                ScannerId = scannerId.Trim(),
                X = xs.ToArray(),
                Y = ys.ToArray(),
                CapturedAt = DateTimeOffset.UtcNow,
                AverageScans = profile.Frames,
                Encoders = encoders,
            };
        }
        finally
        {
            Release(endpointKey, ownerId);
        }
    }

    /// <summary>中止進行中的線掃描。</summary>
    public async Task AbortLineAsync(string taskId, CancellationToken ct = default)
    {
        if (!_captures.TryRemove(taskId, out var capture))
        {
            return;
        }

        Interlocked.Exchange(ref capture.Aborted, 1);
        capture.Stop.Cancel();
        capture.Session.Recorder.Fail("線掃描已中止。");
        await capture.Lease.DisposeAsync().ConfigureAwait(false);
        await capture.Session.Recorder.Stop().ConfigureAwait(false);
        Task? completion;
        lock (capture.Gate)
        {
            completion = capture.Completion;
        }

        if (completion != null)
        {
            try
            {
                await completion.ConfigureAwait(false);
            }
            catch (Exception)
            {
            }
        }

        Release(capture.EndpointKey, taskId);
        ct.ThrowIfCancellationRequested();
    }

    async Task FinalizeCoreAsync(Capture capture)
    {
        try
        {
            await capture.Lease.DisposeAsync().ConfigureAwait(false);
            await capture.Session.StopAndBuildAsync().ConfigureAwait(false);
            capture.Stop.Token.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception error) when (error is ArgumentException or InvalidDataException or IOException)
        {
            throw new LineScanDeviceException("SCAN_LINE_MEASUREMENT", "線掃描無法完成有效錄製：" + error.Message);
        }
        finally
        {
            _captures.TryRemove(capture.TaskId, out _);
            Release(capture.EndpointKey, capture.TaskId);
        }
    }

    string Claim(LineScannerEndpointOptions settings, string taskId)
    {
        string endpoint = settings.Ip.Trim() + ":" + settings.Port;
        lock (_gate)
        {
            if (!_endpoints.TryAdd(endpoint, taskId))
            {
                throw new LineScanDeviceException("SCAN_BUSY", "此掃描器已有量測任務進行中。");
            }
        }

        return endpoint;
    }

    void Release(string endpoint, string taskId)
    {
        lock (_gate)
        {
            if (_endpoints.TryGetValue(endpoint, out var owner) && owner == taskId)
            {
                _endpoints.Remove(endpoint);
            }
        }
    }

    async Task<LineScanLease> OpenAsync(LineScannerEndpointOptions settings, CancellationToken cancellationToken)
    {
        var scanner = new Scanner();
        var lease = new LineScanLease(scanner, ownsScanner: true);
        try
        {
            scanner.Start(settings.Ip.Trim(), settings.Port);
            await lease.First.WaitAsync(TimeSpan.FromSeconds(settings.ConnectionTimeoutSeconds), cancellationToken)
                .ConfigureAwait(false);
            return lease;
        }
        catch (TimeoutException)
        {
            await lease.DisposeAsync().ConfigureAwait(false);
            throw new LineScanDeviceException(
                "SCAN_DEVICE_TIMEOUT",
                "逾時仍未收到有效 SICK 掃描資料，請檢查設備連線與資料輸出。");
        }
        catch
        {
            await lease.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    static bool Inside(P point, double[]? roi)
    {
        if (roi == null)
        {
            return true;
        }

        return point.X >= roi[0] && point.X <= roi[1] && point.Y >= roi[2] && point.Y <= roi[3];
    }

    sealed class SetupPreview
    {
        public readonly string ScannerId;
        public readonly string EndpointKey;
        public readonly LineScanLease Lease;

        public SetupPreview(string scannerId, string endpointKey, LineScanLease lease)
        {
            ScannerId = scannerId;
            EndpointKey = endpointKey;
            Lease = lease;
        }
    }

    sealed class Capture
    {
        public readonly string TaskId;
        public readonly string ScannerId;
        public readonly string EndpointKey;
        public readonly Calibration Calibration;
        public readonly ReconstructionOptions Reconstruction;
        public readonly LineScanLease Lease;
        public readonly EncoderScanSession Session;
        public readonly CancellationTokenSource Stop = new();
        public readonly object Gate = new();
        public Task? Completion;
        public int Aborted;

        public Capture(
            string taskId,
            string scannerId,
            string endpointKey,
            Calibration calibration,
            ReconstructionOptions reconstruction,
            LineScanLease lease,
            EncoderScanSession session)
        {
            TaskId = taskId;
            ScannerId = scannerId;
            EndpointKey = endpointKey;
            Calibration = calibration;
            Reconstruction = reconstruction;
            Lease = lease;
            Session = session;
        }
    }
}
