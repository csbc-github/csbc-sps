using Microsoft.EntityFrameworkCore;
using ThreeDScan.Domain.Models;
using ThreeDScan.Domain.Repositories;
using ThreeDScan.Infrastructure.Repositories.Models;

namespace ThreeDScan.Infrastructure.Repositories;

/// <summary>滾輪校正基準存於 RollerCalibration。</summary>
public sealed class RollerCalibrationRepository : IRollerCalibrationRepository
{
    private readonly ThreeDScanDbContext _db;

    /// <summary>建立滾輪校正儲存庫。</summary>
    public RollerCalibrationRepository(ThreeDScanDbContext db)
    {
        _db = db;
    }

    /// <inheritdoc />
    public async Task<RollerBaselineRecord?> GetAsync(string scannerId, CancellationToken ct = default)
    {
        var id = scannerId.Trim();
        var row = await _db.RollerCalibrations
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.ScannerId == id, ct)
            .ConfigureAwait(false);
        if (row is null)
        {
            return null;
        }

        return new RollerBaselineRecord
        {
            ScannerId = row.ScannerId,
            ZDistance = row.ZDistance,
            LineA = row.LineA,
            LineB = row.LineB,
            LineC = row.LineC,
            CalibratedAt = row.CalibratedAt,
            TraceId = row.TraceId,
        };
    }

    /// <inheritdoc />
    public async Task SaveAsync(RollerBaselineRecord record, CancellationToken ct = default)
    {
        var id = record.ScannerId.Trim();
        var row = await _db.RollerCalibrations
            .FirstOrDefaultAsync(x => x.ScannerId == id, ct)
            .ConfigureAwait(false);
        if (row is null)
        {
            row = new RollerCalibrationEntity
            {
                ScannerId = id,
            };
            _db.RollerCalibrations.Add(row);
        }

        row.ZDistance = record.ZDistance;
        row.LineA = record.LineA;
        row.LineB = record.LineB;
        row.LineC = record.LineC;
        row.CalibratedAt = record.CalibratedAt;
        row.TraceId = record.TraceId;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
