using ThreeDScan.Domain.Models;

namespace ThreeDScan.Domain.Abstractions;

/// <summary>線掃描裝置：架設預覽、profile 與即時輪廓快取。</summary>
public interface ILineScanDeviceFacade
{
    /// <summary>開始架設長連預覽（O8）。</summary>
    Task StartSetupPreviewAsync(string scannerId, CancellationToken ct = default);

    /// <summary>結束架設預覽。</summary>
    Task StopSetupPreviewAsync(string scannerId);

    /// <summary>單次凍結輪廓。</summary>
    Task<LineScanProfileData> CaptureProfileAsync(string scannerId, string traceId, CancellationToken ct = default);

    /// <summary>架設預覽最新一圈 XY。</summary>
    bool TryGetSetupLive(string scannerId, out double[] x, out double[] y);

    /// <summary>錄製中任務最新一圈 XY。</summary>
    bool TryGetRecordingLive(string taskId, out double[] x, out double[] y);
}
