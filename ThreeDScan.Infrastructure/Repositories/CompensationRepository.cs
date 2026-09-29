using Microsoft.EntityFrameworkCore;
using ThreeDScan.Domain.Repositories;
using ThreeDScan.Domain.Models;
using ThreeDScan.Infrastructure.Repositories.Models;

namespace ThreeDScan.Infrastructure.Repositories;

/// <summary>儀器校正補償歷程資料庫存取（EF Core）。</summary>
public sealed class CompensationRepository : ICompensationRepository
{
    private readonly ThreeDScanDbContext _db;

    /// <summary>建立補償歷程資料庫存取。</summary>
    public CompensationRepository(ThreeDScanDbContext db)
    {
        _db = db;
    }

    /// <inheritdoc />
    public async Task<CompensationOffset?> GetLatestAsync(string scannerId, CancellationToken ct = default)
    {
        var row = await _db.CompensationHistory
            .AsNoTracking()
            .Where(x => x.ScannerId == scannerId)
            .OrderByDescending(x => x.CalibratedAt)
            .FirstOrDefaultAsync(ct);

        return row is null ? null : Map(row);
    }

    /// <inheritdoc />
    public async Task AppendAsync(CompensationOffset offset, CancellationToken ct = default)
    {
        _db.CompensationHistory.Add(new CompensationHistoryEntity
        {
            ScannerId = offset.ScannerId,
            OffsetX = offset.OffsetX,
            OffsetY = offset.OffsetY,
            OffsetZ = offset.OffsetZ,
            Rmse = offset.Rmse,
            MachineArea = offset.MachineArea,
            CalibratedAt = offset.CalibratedAt,
            Operator = offset.Operator,
            TraceId = offset.TraceId,
        });
        await _db.SaveChangesAsync(ct);
    }

    /// <inheritdoc />
    public async Task<bool> HasAsync(string scannerId, CancellationToken ct = default)
    {
        return await _db.CompensationHistory.AnyAsync(x => x.ScannerId == scannerId, ct);
    }

    private static CompensationOffset Map(CompensationHistoryEntity row)
    {
        return new()
        {
            ScannerId = row.ScannerId,
            OffsetX = row.OffsetX,
            OffsetY = row.OffsetY,
            OffsetZ = row.OffsetZ,
            Rmse = row.Rmse,
            MachineArea = row.MachineArea,
            CalibratedAt = row.CalibratedAt,
            Operator = row.Operator,
            TraceId = row.TraceId,
        };
    }
}
