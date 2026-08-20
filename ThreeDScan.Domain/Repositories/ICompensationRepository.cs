using ThreeDScan.Domain.Models;

namespace ThreeDScan.Domain.Repositories;

/// <summary>儀器校正補償值（CompensationHistory）持久化。</summary>
public interface ICompensationRepository
{
    /// <summary>取得該掃描器最新一筆補償記錄；無歷程時回傳 null。</summary>
    Task<CompensationOffset?> GetLatestAsync(string scannerId, CancellationToken ct = default);

    /// <summary>追加一筆校正補償記錄（不覆寫舊資料）。</summary>
    Task AppendAsync(CompensationOffset offset, CancellationToken ct = default);

    /// <summary>該掃描器是否已有至少一筆補償歷程。</summary>
    Task<bool> HasAsync(string scannerId, CancellationToken ct = default);
}
