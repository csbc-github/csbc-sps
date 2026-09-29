using Microsoft.EntityFrameworkCore;
using ThreeDScan.Domain.Repositories;
using ThreeDScan.Domain.Models;
using ThreeDScan.Infrastructure.Repositories.Models;

namespace ThreeDScan.Infrastructure.Repositories;

/// <summary>掃描器拍攝群組顯示名稱資料庫存取（EF Core）。</summary>
public sealed class ScannerProgramGroupRepository : IScannerProgramGroupRepository
{
    private readonly ThreeDScanDbContext _db;

    /// <summary>建立拍攝群組名稱資料庫存取。</summary>
    public ScannerProgramGroupRepository(ThreeDScanDbContext db)
    {
        _db = db;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ScannerProgramGroup>> GetOrCreateAsync(
        string scannerId,
        CancellationToken ct = default)
    {
        var existing = await _db.ScannerProgramGroupNames
            .AsNoTracking()
            .Where(x => x.ScannerId == scannerId)
            .ToListAsync(ct);

        if (existing.Count < ScannerProgramGroupDefaults.GroupCount)
        {
            var now = DateTimeOffset.UtcNow;
            for (byte programNo = 0; programNo < ScannerProgramGroupDefaults.GroupCount; programNo++)
            {
                if (existing.Any(x => x.ProgramNo == programNo)) continue;

                _db.ScannerProgramGroupNames.Add(new ScannerProgramGroupNameEntity
                {
                    ScannerId = scannerId,
                    ProgramNo = programNo,
                    DisplayName = ScannerProgramGroupDefaults.DisplayNameFor(programNo),
                    UpdatedAt = now,
                    UpdatedBy = "system",
                });
            }

            await _db.SaveChangesAsync(ct);
            existing = await _db.ScannerProgramGroupNames
                .AsNoTracking()
                .Where(x => x.ScannerId == scannerId)
                .ToListAsync(ct);
        }

        return existing
            .OrderBy(x => x.ProgramNo)
            .Select(x => new ScannerProgramGroup
            {
                ProgramNo = x.ProgramNo,
                DisplayName = x.DisplayName,
            })
            .ToList();
    }

    /// <inheritdoc />
    public async Task UpdateDisplayNameAsync(
        string scannerId,
        byte programNo,
        string displayName,
        string? updatedBy,
        CancellationToken ct = default)
    {
        var trimmed = displayName.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            throw new ArgumentException("群組名稱不可為空白。", nameof(displayName));

        var row = await _db.ScannerProgramGroupNames
            .FirstOrDefaultAsync(x => x.ScannerId == scannerId && x.ProgramNo == programNo, ct);

        if (row is null)
        {
            _db.ScannerProgramGroupNames.Add(new ScannerProgramGroupNameEntity
            {
                ScannerId = scannerId,
                ProgramNo = programNo,
                DisplayName = trimmed,
                UpdatedAt = DateTimeOffset.UtcNow,
                UpdatedBy = updatedBy,
            });
        }
        else
        {
            row.DisplayName = trimmed;
            row.UpdatedAt = DateTimeOffset.UtcNow;
            row.UpdatedBy = updatedBy;
        }

        await _db.SaveChangesAsync(ct);
    }
}
