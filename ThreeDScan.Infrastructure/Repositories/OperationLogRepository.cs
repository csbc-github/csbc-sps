using ThreeDScan.Domain.Repositories;
using ThreeDScan.Domain.Models;
using ThreeDScan.Infrastructure.Repositories.Models;

namespace ThreeDScan.Infrastructure.Repositories;

/// <summary>操作稽核日誌資料庫存取（EF Core）。</summary>
public sealed class OperationLogRepository : IOperationLogRepository
{
    private readonly ThreeDScanDbContext _db;

    public OperationLogRepository(ThreeDScanDbContext db)
    {
        _db = db;
    }

    /// <inheritdoc />
    public async Task AppendAsync(OperationLogEntry entry, CancellationToken ct = default)
    {
        _db.OperationLogs.Add(new OperationLogEntity
        {
            Timestamp = entry.Timestamp,
            TraceId = entry.TraceId,
            ClientId = entry.ClientId,
            Operator = entry.Operator,
            Action = entry.Action,
            Target = entry.Target,
            Result = entry.Result,
            ErrorCode = entry.ErrorCode,
            Detail = entry.Detail,
        });
        await _db.SaveChangesAsync(ct);
    }
}
