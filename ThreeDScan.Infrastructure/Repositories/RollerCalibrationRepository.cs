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
            CalibratedAt = row.CalibratedAt,
            TraceId = row.TraceId,
        };
    }
}
