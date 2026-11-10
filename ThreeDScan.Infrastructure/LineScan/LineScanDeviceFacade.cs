using ThreeDScan.Domain.Abstractions;
using ThreeDScan.Domain.Models;
using ThreeDScan.Infrastructure.LineScan.Models;

namespace ThreeDScan.Infrastructure.LineScan;

/// <summary><see cref="ILineScanDeviceFacade"/> 實作。</summary>
public sealed class LineScanDeviceFacade : ILineScanDeviceFacade
{
    private readonly LineScanDeviceHost _host;

    /// <summary>建立門面。</summary>
    public LineScanDeviceFacade(LineScanDeviceHost host)
    {
        _host = host;
    }

    /// <inheritdoc />
    public async Task StartSetupPreviewAsync(string scannerId, CancellationToken ct = default)
    {
        try
        {
            await _host.StartSetupPreviewAsync(scannerId, ct).ConfigureAwait(false);
        }
        catch (LineScanDeviceException ex)
        {
            throw new ScanTaskException(400, ex.Code, ex.Message);
        }
    }

    /// <inheritdoc />
    public Task StopSetupPreviewAsync(string scannerId)
    {
        return _host.StopSetupPreviewAsync(scannerId);
    }

    /// <inheritdoc />
    public async Task<LineScanProfileData> CaptureProfileAsync(string scannerId, string traceId, CancellationToken ct = default)
    {
        try
        {
            LineScanProfileCaptureResult raw = await _host.CaptureProfileAsync(scannerId, ct).ConfigureAwait(false);
            return MapProfile(raw, traceId);
        }
        catch (LineScanDeviceException ex)
        {
            throw new ScanTaskException(400, ex.Code, ex.Message);
        }
    }

    /// <inheritdoc />
    public bool TryGetSetupLive(string scannerId, out double[] x, out double[] y)
    {
        return _host.TryGetSetupLive(scannerId, out x, out y);
    }

    /// <inheritdoc />
    public bool TryGetRecordingLive(string taskId, out double[] x, out double[] y)
    {
        return _host.TryGetRecordingLive(taskId, out x, out y);
    }

    private static LineScanProfileData MapProfile(LineScanProfileCaptureResult raw, string traceId)
    {
        var encoders = new List<LineScanEncoderData>();
        foreach (var e in raw.Encoders)
        {
            encoders.Add(new LineScanEncoderData
            {
                Position = e.Position,
                Reserved = e.Reserved,
            });
        }

        return new LineScanProfileData
        {
            ScannerId = raw.ScannerId,
            X = raw.X,
            Y = raw.Y,
            CapturedAt = raw.CapturedAt.ToString("O"),
            AverageScans = raw.AverageScans,
            TraceId = traceId,
            Encoders = encoders,
        };
    }
}
