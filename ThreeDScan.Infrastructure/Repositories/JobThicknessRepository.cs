using Microsoft.EntityFrameworkCore;
using ThreeDScan.Domain.Models;
using ThreeDScan.Domain.Repositories;
using ThreeDScan.Infrastructure.Repositories.Models;

namespace ThreeDScan.Infrastructure.Repositories;

/// <summary>
/// 工單兩翼厚度的 EF Core 存取。對應表 <c>JobThickness</c>（schema-v1.8）。
/// </summary>
public sealed class JobThicknessRepository : IJobThicknessRepository
{
    private readonly ThreeDScanDbContext _db;

    /// <summary>
    /// 建立倉儲；由 DI 注入同一請求範圍的 <see cref="ThreeDScanDbContext"/>。
    /// </summary>
    /// <param name="db">ThreeDScan 資料庫內容。</param>
    public JobThicknessRepository(ThreeDScanDbContext db)
    {
        _db = db;
    }

    /// <inheritdoc />
    /// <remarks>
    /// 以主鍵 <c>JobId</c> 精確比對（先 Trim）。找不到或傳入空白工單號時回 <c>null</c>，
    /// 不拋例外，由面掃描服務轉成 <c>SCAN_THK_002</c>。
    /// </remarks>
    public async Task<JobThicknessRecord?> GetByJobIdAsync(string jobId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(jobId))
        {
            return null;
        }

        var row = await _db.JobThickness
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.JobId == jobId.Trim(), ct)
            .ConfigureAwait(false);
        if (row is null)
        {
            return null;
        }

        return Map(row);
    }

    /// <inheritdoc />
    /// <remarks>
    /// 無列則 INSERT；已有同一 <c>JobId</c> 則更新厚度、A／低翼對應、掃描器與追溯欄位。
    /// 噴印區對同一工單重測時會覆寫，割切讀到的永遠是最新噴印結果。
    /// </remarks>
    public async Task UpsertAsync(JobThicknessRecord record, CancellationToken ct = default)
    {
        var jobId = record.JobId.Trim();
        var row = await _db.JobThickness
            .FirstOrDefaultAsync(x => x.JobId == jobId, ct)
            .ConfigureAwait(false);
        if (row is null)
        {
            _db.JobThickness.Add(new JobThicknessEntity
            {
                JobId = jobId,
                ThicknessA = record.ThicknessA,
                ThicknessB = record.ThicknessB,
                ThicknessAOnLowFlange = record.ThicknessAOnLowFlange,
                ScannerId = record.ScannerId,
                MeasuredAt = record.MeasuredAt,
                TraceId = record.TraceId,
            });
        }
        else
        {
            row.ThicknessA = record.ThicknessA;
            row.ThicknessB = record.ThicknessB;
            row.ThicknessAOnLowFlange = record.ThicknessAOnLowFlange;
            row.ScannerId = record.ScannerId;
            row.MeasuredAt = record.MeasuredAt;
            row.TraceId = record.TraceId;
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 將資料表列轉成網域紀錄，供應用層使用。
    /// </summary>
    /// <param name="row">EF 實體列。</param>
    /// <returns>對應的 <see cref="JobThicknessRecord"/>。</returns>
    private static JobThicknessRecord Map(JobThicknessEntity row)
    {
        return new JobThicknessRecord
        {
            JobId = row.JobId,
            ThicknessA = row.ThicknessA,
            ThicknessB = row.ThicknessB,
            ThicknessAOnLowFlange = row.ThicknessAOnLowFlange,
            ScannerId = row.ScannerId,
            MeasuredAt = row.MeasuredAt,
            TraceId = row.TraceId,
        };
    }
}
