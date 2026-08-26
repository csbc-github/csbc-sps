using ThreeDScan.Domain.Models;
using ThreeDScan.Domain.Repositories;
using ThreeDScan.Infrastructure.Repositories.Models;

namespace ThreeDScan.Infrastructure.Repositories;

/// <summary>掃描 API 呼叫紀錄資料庫存取（EF Core）。只 INSERT。</summary>
public sealed class ScanCallLogRepository : IScanCallLogRepository
{
    private readonly ThreeDScanDbContext _db;

    /// <summary>
    /// 建立倉儲。
    /// </summary>
    /// <param name="db">ThreeDScan 資料庫內容。</param>
    public ScanCallLogRepository(ThreeDScanDbContext db)
    {
        _db = db;
    }

    /// <inheritdoc />
    /// <remarks>不更新既有列。</remarks>
    public async Task AppendAsync(ScanCallLogRecord record, CancellationToken ct = default)
    {
        _db.ScanCallLogs.Add(new ScanCallLogEntity
        {
            Timestamp = record.Timestamp,
            TraceId = record.TraceId,
            Action = record.Action,
            ScannerId = record.ScannerId,
            JobId = record.JobId,
            TaskId = record.TaskId,
            Seq = record.Seq,
            Success = record.Success,
            ErrorCode = record.ErrorCode,
            ErrorMessage = record.ErrorMessage,
            RequestJson = record.RequestJson,
            ResponseJson = record.ResponseJson,
            PointCloudPath = record.PointCloudPath,
            PointCloudSha256 = record.PointCloudSha256,
            ZipPath = record.ZipPath,
            ZipSha256 = record.ZipSha256,
        });
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
