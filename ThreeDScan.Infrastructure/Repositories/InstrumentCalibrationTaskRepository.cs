using Microsoft.EntityFrameworkCore;
using ThreeDScan.Domain.Repositories;
using ThreeDScan.Domain.Models;
using ThreeDScan.Infrastructure.Repositories.Models;

namespace ThreeDScan.Infrastructure.Repositories;

/// <summary>UC-01 兩次拍攝任務資料庫存取。</summary>
public sealed class InstrumentCalibrationTaskRepository : IInstrumentCalibrationTaskRepository
{
    private readonly ThreeDScanDbContext _db;

    public InstrumentCalibrationTaskRepository(ThreeDScanDbContext db)
    {
        _db = db;
    }

    /// <inheritdoc />
    public async Task CreateAsync(InstrumentCalibrationTaskRecord task, CancellationToken ct = default)
    {
        _db.InstrumentCalibrationTasks.Add(new InstrumentCalibrationTaskEntity
        {
            TaskId = task.TaskId,
            ScannerId = task.ScannerId,
            Status = task.Status,
            CreatedAt = task.CreatedAt,
            UpdatedAt = task.UpdatedAt,
            ExpiresAt = task.ExpiresAt,
            ExpectedX = task.ExpectedX,
            ExpectedY = task.ExpectedY,
            ExpectedZ = task.ExpectedZ,
            BallCount = task.BallCount,
            ScannerToFloorZ = task.ScannerToFloorZ,
            ActualX = task.ActualX,
            ActualY = task.ActualY,
            Rmse = task.Rmse,
            FrameJson = task.FrameJson,
            TraceIdSeq1 = task.TraceIdSeq1,
        });
        await _db.SaveChangesAsync(ct);
    }

    /// <inheritdoc />
    public async Task<InstrumentCalibrationTaskRecord?> GetAsync(string taskId, CancellationToken ct = default)
    {
        var row = await _db.InstrumentCalibrationTasks
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.TaskId == taskId, ct);
        return row is null ? null : Map(row);
    }

    /// <inheritdoc />
    public async Task CompleteAsync(string taskId, CancellationToken ct = default)
    {
        var row = await _db.InstrumentCalibrationTasks.FirstOrDefaultAsync(x => x.TaskId == taskId, ct);
        if (row is null)
            return;

        row.Status = InstrumentCalibrationTaskRecord.StatusCompleted;
        row.UpdatedAt = DateTimeOffset.Now;
        await _db.SaveChangesAsync(ct);
    }

    private static InstrumentCalibrationTaskRecord Map(InstrumentCalibrationTaskEntity row)
    {
        return new()
        {
            TaskId = row.TaskId,
            ScannerId = row.ScannerId,
            Status = row.Status,
            CreatedAt = row.CreatedAt,
            UpdatedAt = row.UpdatedAt,
            ExpiresAt = row.ExpiresAt,
            ExpectedX = row.ExpectedX,
            ExpectedY = row.ExpectedY,
            ExpectedZ = row.ExpectedZ,
            BallCount = row.BallCount,
            ScannerToFloorZ = row.ScannerToFloorZ,
            ActualX = row.ActualX,
            ActualY = row.ActualY,
            Rmse = row.Rmse,
            FrameJson = row.FrameJson,
            TraceIdSeq1 = row.TraceIdSeq1,
        };
    }
}
