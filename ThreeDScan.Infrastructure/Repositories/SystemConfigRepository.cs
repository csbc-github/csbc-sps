using Microsoft.EntityFrameworkCore;
using ThreeDScan.Domain.Repositories;
using ThreeDScan.Infrastructure.Repositories.Models;

namespace ThreeDScan.Infrastructure.Repositories;

/// <summary>系統鍵值設定資料庫存取。</summary>
public sealed class SystemConfigRepository : ISystemConfigRepository
{
    private readonly ThreeDScanDbContext _db;

    /// <summary>建立系統設定倉儲。</summary>
    /// <param name="db">資料庫內容。</param>
    public SystemConfigRepository(ThreeDScanDbContext db)
    {
        _db = db;
    }

    /// <inheritdoc />
    public async Task<string?> GetAsync(string key, CancellationToken ct = default)
    {
        var row = await _db.SystemConfigs
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.ConfigKey == key, ct)
            .ConfigureAwait(false);
        return row?.ConfigValue;
    }

    /// <inheritdoc />
    public async Task SetAsync(string key, string value, CancellationToken ct = default)
    {
        var row = await _db.SystemConfigs.FirstOrDefaultAsync(x => x.ConfigKey == key, ct).ConfigureAwait(false);
        if (row is null)
        {
            _db.SystemConfigs.Add(new SystemConfigEntity
            {
                ConfigKey = key,
                ConfigValue = value,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
        }
        else
        {
            row.ConfigValue = value;
            row.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task EnsureMissingAsync(string key, string defaultValue, CancellationToken ct = default)
    {
        var exists = await _db.SystemConfigs.AnyAsync(x => x.ConfigKey == key, ct).ConfigureAwait(false);
        if (exists)
        {
            return;
        }

        _db.SystemConfigs.Add(new SystemConfigEntity
        {
            ConfigKey = key,
            ConfigValue = defaultValue,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
