using Microsoft.EntityFrameworkCore;
using ThreeDScan.Domain.Abstractions;
using ThreeDScan.Domain.Models;
using ThreeDScan.Infrastructure.Data;
using ThreeDScan.Infrastructure.Data.Entities;

namespace ThreeDScan.Infrastructure.Persistence;

/// <summary>ScannerConfig 資料庫存取（EF Core）。</summary>
public sealed class ScannerConfigStore : IScannerConfigStore
{
    private readonly ThreeDScanDbContext _db;

    public ScannerConfigStore(ThreeDScanDbContext db) => _db = db;

    /// <inheritdoc />
    public async Task<ScannerConfigRecord?> GetAsync(string scannerId, CancellationToken ct = default)
    {
        var row = await _db.ScannerConfigs
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.ScannerId == scannerId, ct);

        return row is null ? null : Map(row);
    }

    /// <inheritdoc />
    public async Task UpsertAsync(ScannerConfigRecord record, CancellationToken ct = default)
    {
        var existing = await _db.ScannerConfigs
            .FirstOrDefaultAsync(x => x.ScannerId == record.ScannerId, ct);

        if (existing is null)
        {
            _db.ScannerConfigs.Add(ToEntity(record));
        }
        else
        {
            existing.RefPatternX = record.RefPatternX;
            existing.RefPatternY = record.RefPatternY;
            existing.RefSphereX = record.RefSphereX;
            existing.RefSphereY = record.RefSphereY;
            existing.ContentsFolder = record.ContentsFolder;
            existing.UpdatedAt = record.UpdatedAt;
            existing.UpdatedBy = record.UpdatedBy;
            existing.TraceId = record.TraceId;
            MergeCalibration(existing, record);
        }

        await _db.SaveChangesAsync(ct);
    }

    private static ScannerConfigEntity ToEntity(ScannerConfigRecord record) => new()
    {
        ScannerId = record.ScannerId,
        RefPatternX = record.RefPatternX,
        RefPatternY = record.RefPatternY,
        RefSphereX = record.RefSphereX,
        RefSphereY = record.RefSphereY,
        ContentsFolder = record.ContentsFolder,
        UpdatedAt = record.UpdatedAt,
        UpdatedBy = record.UpdatedBy,
        TraceId = record.TraceId,
        ExpectedBallDiameter = record.ExpectedBallDiameter,
        DiameterTolerance = record.DiameterTolerance,
        MaxDiameterError = record.MaxDiameterError,
        BallCount = record.BallCount,
        DatumPlaneEnabled = record.DatumPlaneEnabled,
        DatumPlaneCenterX = record.DatumPlaneCenterX,
        DatumPlaneCenterY = record.DatumPlaneCenterY,
        DatumPlaneWidth = record.DatumPlaneWidth,
        DatumPlaneHeight = record.DatumPlaneHeight,
        DatumPlaneAngle = record.DatumPlaneAngle,
    };

    private static void MergeCalibration(ScannerConfigEntity target, ScannerConfigRecord source)
    {
        if (source.ExpectedBallDiameter is double d) target.ExpectedBallDiameter = d;
        if (source.DiameterTolerance is double dt) target.DiameterTolerance = dt;
        if (source.MaxDiameterError is double mde) target.MaxDiameterError = mde;
        if (source.BallCount is int bc) target.BallCount = bc;
        if (source.DatumPlaneEnabled is bool dpe) target.DatumPlaneEnabled = dpe;
        if (source.DatumPlaneCenterX is double cx) target.DatumPlaneCenterX = cx;
        if (source.DatumPlaneCenterY is double cy) target.DatumPlaneCenterY = cy;
        if (source.DatumPlaneWidth is double w) target.DatumPlaneWidth = w;
        if (source.DatumPlaneHeight is double h) target.DatumPlaneHeight = h;
        if (source.DatumPlaneAngle is double a) target.DatumPlaneAngle = a;
    }

    private static ScannerConfigRecord Map(ScannerConfigEntity row) => new()
    {
        ScannerId = row.ScannerId,
        RefPatternX = row.RefPatternX,
        RefPatternY = row.RefPatternY,
        RefSphereX = row.RefSphereX,
        RefSphereY = row.RefSphereY,
        ContentsFolder = row.ContentsFolder,
        UpdatedAt = row.UpdatedAt,
        UpdatedBy = row.UpdatedBy,
        TraceId = row.TraceId,
        ExpectedBallDiameter = row.ExpectedBallDiameter,
        DiameterTolerance = row.DiameterTolerance,
        MaxDiameterError = row.MaxDiameterError,
        BallCount = row.BallCount,
        DatumPlaneEnabled = row.DatumPlaneEnabled,
        DatumPlaneCenterX = row.DatumPlaneCenterX,
        DatumPlaneCenterY = row.DatumPlaneCenterY,
        DatumPlaneWidth = row.DatumPlaneWidth,
        DatumPlaneHeight = row.DatumPlaneHeight,
        DatumPlaneAngle = row.DatumPlaneAngle,
    };
}
