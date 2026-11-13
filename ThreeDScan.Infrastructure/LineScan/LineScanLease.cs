using ThreeDScan.Infrastructure.LineScan.Hank;
using ScanPacket = ThreeDScan.Infrastructure.LineScan.Hank.Scan;

namespace ThreeDScan.Infrastructure.LineScan;

/// <summary>SICK TCP 連線租約：首幀、錄製轉送與輪廓預覽。</summary>
public sealed class LineScanLease : IAsyncDisposable
{
    /// <summary>多幀平均輪廓結果。</summary>
    public sealed record ProfileCapture(P?[] Points, ScanPacket LastScan, int Frames);

    readonly object gate = new();
    readonly bool ownsScanner;
    readonly TaskCompletionSource<ScanPacket> first = new(TaskCreationOptions.RunContinuationsAsynchronously);
    Action<ScanPacket>? submit;
    Action<string>? failure;
    ScanPacket? latest;
    double latestAt;
    System.Threading.Timer? watchdog;
    bool stopped;
    bool received;
    Task? disposal;
    PointMean? previewMean;
    ScanPacket? previewPrevious;
    TaskCompletionSource<ProfileCapture>? preview;
    int previewFrames;
    bool liveCacheEnabled;
    int liveAverageScans = 20;
    PointMean liveMean = new();
    ScanPacket? livePrevious;
    double[]? liveX;
    double[]? liveY;

    /// <summary>TCP 掃描器。</summary>
    public Scanner Scanner { get; }

    /// <summary>首幀有效輪廓。</summary>
    public Task<ScanPacket> First
    {
        get { return first.Task; }
    }

    /// <summary>建立租約並訂閱掃描事件。</summary>
    public LineScanLease(Scanner scanner, bool ownsScanner)
    {
        Scanner = scanner;
        this.ownsScanner = ownsScanner;
        scanner.Received += Receive;
        scanner.State += State;
    }

    void Receive(ScanPacket scan)
    {
        lock (gate)
        {
            if (stopped)
            {
                return;
            }

            if (scan.Status != 0)
            {
                latest = null;
                ResetLiveCacheLocked();
                first.TrySetException(new LineScanDeviceException("SCAN_DEVICE_ERROR", $"掃描器狀態異常：{scan.Status}"));
                preview?.TrySetException(new LineScanDeviceException("SCAN_DEVICE_ERROR", $"掃描器狀態異常：{scan.Status}"));
                failure?.Invoke($"掃描器狀態異常：{scan.Status}");
                return;
            }

            if (latest != null && IsDuplicate(scan, latest))
            {
                // Keep the canonical preview frame; raw capture still validates every telegram.
                submit?.Invoke(scan);
                return;
            }
            if (latest == null || latest.Counter != scan.Counter || latest.Timestamp != scan.Timestamp)
            {
                latestAt = EncoderMonitor.Now;
            }

            received = true;
            latest = scan;
            if (scan.Points.Any(point => point != null))
            {
                first.TrySetResult(scan);
            }

            AddPreview(scan);
            UpdateLiveCacheLocked();
            submit?.Invoke(scan);
        }
    }

    /// <summary>開啟平均預覽快取；原始錄製仍接收每一圈。</summary>
    public void EnableLiveCache(int averageScans = 20)
    {
        if (averageScans is < 1 or > 600) throw new ArgumentOutOfRangeException(nameof(averageScans), "平均掃描圈數須為 1～600");
        lock (gate)
        {
            if (liveCacheEnabled && liveAverageScans == averageScans) return;
            liveCacheEnabled = true;
            liveAverageScans = averageScans;
            ResetLiveCacheLocked();
            UpdateLiveCacheLocked();
        }
    }

    /// <summary>讀取最新完成平均的有效點（scanner XY）。</summary>
    public bool TryGetLiveSnapshot(out double[] x, out double[] y)
    {
        lock (gate)
        {
            if (!stopped && latest != null && Scanner.Connected && EncoderMonitor.Now - latestAt <= 1
                && liveX is { Length: > 0 } xs && liveY is { Length: > 0 } ys && xs.Length == ys.Length)
            {
                x = xs;
                y = ys;
                return true;
            }
        }

        x = [];
        y = [];
        return false;
    }

    void UpdateLiveCacheLocked()
    {
        if (!liveCacheEnabled || latest == null)
        {
            return;
        }

        if (livePrevious is ScanPacket previous)
        {
            if (IsDuplicate(latest, previous)) return;
            if (IsDiscontinuous(latest, previous))
            {
                liveMean = new PointMean();
                liveX = null;
                liveY = null;
            }
        }
        livePrevious = latest;
        liveMean.Add(latest.Points);
        if (liveMean.Frames < liveAverageScans) return;
        var points = liveMean.Result();
        liveMean = new PointMean();
        var xs = new List<double>();
        var ys = new List<double>();
        foreach (var point in points)
        {
            if (point is P p && p.Finite)
            {
                xs.Add(p.X);
                ys.Add(p.Y);
            }
        }

        liveX = xs.ToArray();
        liveY = ys.ToArray();
    }

    void ResetLiveCacheLocked()
    {
        liveMean = new PointMean();
        livePrevious = null;
        liveX = null;
        liveY = null;
    }

    static bool IsDuplicate(ScanPacket scan, ScanPacket previous) =>
        scan.Counter == previous.Counter && scan.Timestamp == previous.Timestamp;

    static bool IsDiscontinuous(ScanPacket scan, ScanPacket previous)
    {
        uint elapsed = unchecked(scan.Timestamp - previous.Timestamp);
        bool changedAngles = scan.Angles.Length > 0 && previous.Angles.Length > 0
            && Math.Max(Math.Abs(scan.Angles[0] - previous.Angles[0]), Math.Abs(scan.Angles[^1] - previous.Angles[^1])) > .5;
        return ((scan.Counter - previous.Counter) & 65535) != 1 || elapsed == 0 || elapsed >= 1000000
            || scan.Points.Length != previous.Points.Length || scan.Frequency != previous.Frequency || changedAngles;
    }

    void State(string state)
    {
        lock (gate)
        {
            ResetLiveCacheLocked();
            previewMean = new PointMean();
            previewPrevious = null;
            latest = null;
            if (!stopped && received && state != "已連線")
            {
                latest = null;
                preview?.TrySetException(new LineScanDeviceException("SCAN_DEVICE_ERROR", "輪廓擷取中斷：" + state));
                failure?.Invoke("掃描資料中斷：" + state);
            }
        }
    }

    void AddPreview(ScanPacket scan)
    {
        if (preview == null || preview.Task.IsCompleted || previewMean == null)
        {
            return;
        }

        if (previewPrevious is ScanPacket previous)
        {
            if (IsDuplicate(scan, previous))
            {
                return;
            }

            if (IsDiscontinuous(scan, previous))
            {
                previewMean = new PointMean();
            }
        }

        previewPrevious = scan;
        previewMean.Add(scan.Points);
        if (previewMean.Frames >= previewFrames)
        {
            preview.TrySetResult(new ProfileCapture(previewMean.Result(), scan, previewMean.Frames));
        }
    }

    /// <summary>擷取多幀平均輪廓（滾輪校正等）。</summary>
    public async Task<ProfileCapture> CaptureProfileAsync(int frames, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var pending = new TaskCompletionSource<ProfileCapture>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (gate)
        {
            if (stopped || latest == null || !Scanner.Connected || EncoderMonitor.Now - latestAt > 1)
            {
                throw new LineScanDeviceException("SCAN_DEVICE_ERROR", "沒有新鮮的有效輪廓資料");
            }

            if (preview != null)
            {
                throw new LineScanDeviceException("SCAN_BUSY", "此連線已有輪廓擷取進行中");
            }

            previewFrames = frames;
            previewMean = new PointMean();
            previewPrevious = null;
            preview = pending;
            AddPreview(latest);
        }

        try
        {
            return await pending.Task.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            throw new LineScanDeviceException("SCAN_DEVICE_TIMEOUT", "輪廓平均擷取逾時，請確認掃描器持續輸出有效資料");
        }
        finally
        {
            lock (gate)
            {
                if (ReferenceEquals(preview, pending))
                {
                    preview = null;
                    previewMean = null;
                    previewPrevious = null;
                }
            }
        }
    }

    /// <summary>綁定 Encoder 錄製工作階段並送出基準幀。</summary>
    public void Attach(EncoderScanSession session)
    {
        lock (gate)
        {
            if (stopped || latest == null || !Scanner.Connected || EncoderMonitor.Now - latestAt > 1)
            {
                throw new LineScanDeviceException("SCAN_DEVICE_ERROR", "掃描連線已中斷或沒有新鮮的有效資料");
            }

            submit = session.Submit;
            failure = session.Recorder.Fail;
            session.Submit(latest);
            watchdog = new System.Threading.Timer(_ =>
            {
                lock (gate)
                {
                    if (!stopped && EncoderMonitor.Now - latestAt > 1)
                    {
                        failure?.Invoke("線掃描超過 1 秒未收到新的有效掃描封包。");
                    }
                }
            }, null, 250, 250);
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        lock (gate)
        {
            if (disposal != null)
            {
                return new ValueTask(disposal);
            }

            if (submit != null && EncoderMonitor.Now - latestAt > 1)
            {
                failure?.Invoke("線掃描結束時超過 1 秒未收到新的有效掃描封包。");
            }

            stopped = true;
            ResetLiveCacheLocked();
            submit = null;
            failure = null;
            watchdog?.Dispose();
            watchdog = null;
            Scanner.Received -= Receive;
            Scanner.State -= State;
            first.TrySetCanceled();
            preview?.TrySetCanceled();
            disposal = ownsScanner ? Scanner.DisposeAsync().AsTask() : Task.CompletedTask;
            return new ValueTask(disposal);
        }
    }
}
