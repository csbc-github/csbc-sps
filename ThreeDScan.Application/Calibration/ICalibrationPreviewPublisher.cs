using ThreeDScan.Domain.Models;

namespace ThreeDScan.Application.Calibration;

/// <summary>校正結果即時推送（WPF 監看／SignalR）。</summary>
public interface ICalibrationPreviewPublisher
{
    /// <summary>推送一筆校正預覽事件至所有訂閱端。</summary>
    Task PublishAsync(CalibrationPreview preview, CancellationToken ct = default);
}
