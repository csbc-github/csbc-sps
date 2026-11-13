using Microsoft.Extensions.Options;
using ThreeDScan.Infrastructure.Configuration;
using ThreeDScan.Infrastructure.LineScan.Hank;

namespace ThreeDScan.Infrastructure.LineScan;

/// <summary>依 JSON 選擇實際或模擬 Encoder，並凍結每次錄製使用的設定。</summary>
public sealed class LineScanEncoderSimulationPolicy
{
    /// <summary>是否使用模擬 Encoder。</summary>
    public bool Enabled => ForwardSpeedMmPerSec.HasValue;

    /// <summary>模擬速度；null 表示使用實際 Encoder。</summary>
    public double? ForwardSpeedMmPerSec { get; }

    /// <summary>只讀設定開關，不檢查 Debug／Release 或環境名稱。</summary>
    public LineScanEncoderSimulationPolicy(IOptions<LineScanEncoderSimulationOptions> options)
    {
        if (!options.Value.Enabled) return;
        double speed = options.Value.ForwardSpeedMmPerSec;
        if (!double.IsFinite(speed) || speed <= 0)
            throw new ArgumentException("EncoderSimulation.ForwardSpeedMmPerSec 必須是大於 0 的有限數值。");
        ForwardSpeedMmPerSec = speed;
    }

    /// <summary>以目前設定建立錄製；實際模式不補入任何 Encoder 位移。</summary>
    public EncoderScanSession StartSession(string rawPath, Calibration calibration,
        ReconstructionOptions settings, object? captureContext = null)
        => EncoderScanSession.Start(rawPath, calibration, settings, captureContext,
            simulatedForwardSpeedMmPerSec: ForwardSpeedMmPerSec);
}
