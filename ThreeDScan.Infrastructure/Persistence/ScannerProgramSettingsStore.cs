using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using ThreeDScan.Domain.Abstractions;
using ThreeDScan.Infrastructure.Data;
using ThreeDScan.Infrastructure.Data.Entities;

namespace ThreeDScan.Infrastructure.Persistence;

/// <summary>掃描器拍攝參數 Blob 與歷程資料庫存取（EF Core）。</summary>
public sealed class ScannerProgramSettingsStore : IScannerProgramSettingsStore
{
    private readonly ThreeDScanDbContext _db;

    public ScannerProgramSettingsStore(ThreeDScanDbContext db) => _db = db;

    /// <inheritdoc />
    public async Task<byte[]?> GetBlobAsync(string scannerId, byte programNo, CancellationToken ct = default)
    {
        var row = await _db.ScannerProgramSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.ScannerId == scannerId && x.ProgramNo == programNo, ct);
        return row?.SettingsBlob;
    }

    /// <inheritdoc />
    public async Task UpsertWithHistoryAsync(
        string scannerId,
        byte programNo,
        byte[] settingsBlob,
        string source,
        string? updatedBy,
        string traceId,
        CancellationToken ct = default)
    {
        var hash = SHA256.HashData(settingsBlob);
        var now = DateTimeOffset.UtcNow;

        _db.ScannerProgramSettingsHistory.Add(new ScannerProgramSettingsHistoryEntity
        {
            ScannerId = scannerId,
            ProgramNo = programNo,
            SettingsBlob = settingsBlob,
            SettingsHash = hash,
            SavedAt = now,
            UpdatedBy = updatedBy,
            TraceId = traceId,
            Source = source,
        });

        var existing = await _db.ScannerProgramSettings
            .FirstOrDefaultAsync(x => x.ScannerId == scannerId && x.ProgramNo == programNo, ct);

        if (existing is null)
        {
            _db.ScannerProgramSettings.Add(new ScannerProgramSettingsEntity
            {
                ScannerId = scannerId,
                ProgramNo = programNo,
                SettingsBlob = settingsBlob,
                SettingsHash = hash,
                UpdatedAt = now,
                UpdatedBy = updatedBy,
                TraceId = traceId,
                Source = source,
            });
        }
        else
        {
            existing.SettingsBlob = settingsBlob;
            existing.SettingsHash = hash;
            existing.UpdatedAt = now;
            existing.UpdatedBy = updatedBy;
            existing.TraceId = traceId;
            existing.Source = source;
        }

        await _db.SaveChangesAsync(ct);
    }
}
