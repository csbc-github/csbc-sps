using ThreeDScan.Application.Scan.Models;
using ThreeDScan.Domain.Abstractions;
using ThreeDScan.Domain.Models;
using ThreeDScan.Domain.Repositories;
namespace ThreeDScan.Application.Scan;

/// <summary>線掃描器兩點／ROI 設定（DB，對齊 Hank 擴充 API）。</summary>
public sealed class LineScannerSettingsAppService
{
    private readonly ILineScannerRegistry _lineScanners;
    private readonly ILineScannerSettingsRepository _settings;
    private readonly IScanTaskRepository _scanTasks;

    /// <summary>建立設定服務。</summary>
    public LineScannerSettingsAppService(
        ILineScannerRegistry lineScanners,
        ILineScannerSettingsRepository settings,
        IScanTaskRepository scanTasks)
    {
        _lineScanners = lineScanners;
        _settings = settings;
        _scanTasks = scanTasks;
    }

    /// <summary>讀取設定。</summary>
    public async Task<LineScannerSettingsDto?> GetAsync(string scannerId, string traceId, CancellationToken ct = default)
    {
        if (!_lineScanners.TryGet(scannerId, out _))
        {
            return null;
        }

        var record = await _settings.GetAsync(scannerId, ct).ConfigureAwait(false);
        return ToDto(scannerId, record, traceId);
    }

    /// <summary>寫入兩點桌面校正。</summary>
    public async Task<LineScannerSettingsDto?> SetTwoPointAsync(
        string scannerId,
        LineScannerTwoPointRequest request,
        string traceId,
        CancellationToken ct = default)
    {
        if (!_lineScanners.TryGet(scannerId, out _))
        {
            return null;
        }

        if (await ScannerBusyAsync(scannerId, ct).ConfigureAwait(false))
        {
            throw new InvalidOperationException("SCANNER_BUSY");
        }

        var firstX = request.First.X;
        var firstY = request.First.Y;
        var secondX = request.Second.X;
        var secondY = request.Second.Y;
        if (!double.IsFinite(firstX) || !double.IsFinite(firstY) || !double.IsFinite(secondX) || !double.IsFinite(secondY))
        {
            throw new ArgumentException("基準線座標必須是有限數值");
        }

        if (firstX > secondX)
        {
            (firstX, secondX) = (secondX, firstX);
            (firstY, secondY) = (secondY, firstY);
        }

        var distance = Math.Sqrt(Math.Pow(secondX - firstX, 2) + Math.Pow(secondY - firstY, 2));
        if (distance < 10)
        {
            throw new ArgumentException("桌面校正需兩個相距至少 10 mm 的有效點");
        }

        var existing = await _settings.GetAsync(scannerId, ct).ConfigureAwait(false)
            ?? new LineScannerSettingsRecord { ScannerId = scannerId.Trim() };
        existing.CalibrationFirstX = firstX;
        existing.CalibrationFirstY = firstY;
        existing.CalibrationSecondX = secondX;
        existing.CalibrationSecondY = secondY;
        existing.UpdatedAt = DateTimeOffset.UtcNow;
        existing.TraceId = traceId;
        await _settings.SaveAsync(existing, ct).ConfigureAwait(false);
        return ToDto(scannerId, existing, traceId);
    }

    /// <summary>寫入或清除 ROI。</summary>
    public async Task<LineScannerSettingsDto?> SetRoiAsync(
        string scannerId,
        LineScannerRoiRequest request,
        string traceId,
        CancellationToken ct = default)
    {
        if (!_lineScanners.TryGet(scannerId, out _))
        {
            return null;
        }

        if (await ScannerBusyAsync(scannerId, ct).ConfigureAwait(false))
        {
            throw new InvalidOperationException("SCANNER_BUSY");
        }

        double[]? bounds = request.Bounds;
        if (bounds is not null)
        {
            if (bounds.Length != 4 || bounds.Any(v => !double.IsFinite(v)) || bounds[0] >= bounds[1] || bounds[2] >= bounds[3])
            {
                throw new ArgumentException("框選的最小座標必須小於最大座標");
            }
        }

        var existing = await _settings.GetAsync(scannerId, ct).ConfigureAwait(false)
            ?? new LineScannerSettingsRecord { ScannerId = scannerId.Trim() };
        if (bounds is null)
        {
            existing.RoiXMin = null;
            existing.RoiXMax = null;
            existing.RoiYMin = null;
            existing.RoiYMax = null;
        }
        else
        {
            existing.RoiXMin = bounds[0];
            existing.RoiXMax = bounds[1];
            existing.RoiYMin = bounds[2];
            existing.RoiYMax = bounds[3];
        }

        existing.UpdatedAt = DateTimeOffset.UtcNow;
        existing.TraceId = traceId;
        await _settings.SaveAsync(existing, ct).ConfigureAwait(false);
        return ToDto(scannerId, existing, traceId);
    }

    private async Task<bool> ScannerBusyAsync(string scannerId, CancellationToken ct)
    {
        foreach (var id in await _scanTasks.NonTerminalTaskIdsAsync(ct).ConfigureAwait(false))
        {
            var doc = await _scanTasks.FindAsync(id, ct).ConfigureAwait(false);
            if (doc is not null
                && string.Equals(doc.ScannerId, scannerId, StringComparison.OrdinalIgnoreCase)
                && (doc.Kind == "Line" || doc.Phase is "LineAcquiring" or "LineProcessing"))
            {
                return true;
            }
        }

        return false;
    }

    private static LineScannerSettingsDto ToDto(string scannerId, LineScannerSettingsRecord? record, string traceId)
    {
        LineScannerCalibrationDto? calibration = null;
        if (record is not null && record.HasCalibration)
        {
            calibration = new LineScannerCalibrationDto
            {
                First = new LineScannerPointDto { X = record.CalibrationFirstX!.Value, Y = record.CalibrationFirstY!.Value },
                Second = new LineScannerPointDto { X = record.CalibrationSecondX!.Value, Y = record.CalibrationSecondY!.Value },
            };
        }

        return new LineScannerSettingsDto
        {
            ScannerId = scannerId.Trim(),
            Calibration = calibration,
            RoiXY = record?.RoiBoundsOrNull(),
            TraceId = traceId,
        };
    }
}
