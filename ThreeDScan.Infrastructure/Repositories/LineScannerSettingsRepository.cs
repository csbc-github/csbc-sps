using Microsoft.EntityFrameworkCore;
using ThreeDScan.Domain.Models;
using ThreeDScan.Domain.Repositories;
using ThreeDScan.Infrastructure.Repositories.Models;

namespace ThreeDScan.Infrastructure.Repositories;

/// <summary>線掃描器兩點與 ROI 的 EF 實作。</summary>
public sealed class LineScannerSettingsRepository : ILineScannerSettingsRepository
{
    private readonly ThreeDScanDbContext _db;

    /// <summary>建立儲存庫。</summary>
    public LineScannerSettingsRepository(ThreeDScanDbContext db)
    {
        _db = db;
    }

    /// <inheritdoc />
    public async Task<LineScannerSettingsRecord?> GetAsync(string scannerId, CancellationToken ct = default)
    {
        var id = scannerId.Trim();
        var row = await _db.LineScannerSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.ScannerId == id, ct)
            .ConfigureAwait(false);
        if (row is null)
        {
            return null;
        }

        return ToRecord(row);
    }

    /// <inheritdoc />
    public async Task SaveAsync(LineScannerSettingsRecord record, CancellationToken ct = default)
    {
        var id = record.ScannerId.Trim();
        var row = await _db.LineScannerSettings
            .FirstOrDefaultAsync(x => x.ScannerId == id, ct)
            .ConfigureAwait(false);
        if (row is null)
        {
            row = new LineScannerSettingsEntity { ScannerId = id };
            _db.LineScannerSettings.Add(row);
        }

        row.CalibrationFirstX = record.CalibrationFirstX;
        row.CalibrationFirstY = record.CalibrationFirstY;
        row.CalibrationSecondX = record.CalibrationSecondX;
        row.CalibrationSecondY = record.CalibrationSecondY;
        row.RoiXMin = record.RoiXMin;
        row.RoiXMax = record.RoiXMax;
        row.RoiYMin = record.RoiYMin;
        row.RoiYMax = record.RoiYMax;
        row.UpdatedAt = record.UpdatedAt;
        row.TraceId = record.TraceId;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private static LineScannerSettingsRecord ToRecord(LineScannerSettingsEntity row)
    {
        return new LineScannerSettingsRecord
        {
            ScannerId = row.ScannerId,
            CalibrationFirstX = row.CalibrationFirstX,
            CalibrationFirstY = row.CalibrationFirstY,
            CalibrationSecondX = row.CalibrationSecondX,
            CalibrationSecondY = row.CalibrationSecondY,
            RoiXMin = row.RoiXMin,
            RoiXMax = row.RoiXMax,
            RoiYMin = row.RoiYMin,
            RoiYMax = row.RoiYMax,
            UpdatedAt = row.UpdatedAt,
            TraceId = row.TraceId,
        };
    }
}
