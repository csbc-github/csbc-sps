using ThreeDScan.Domain.Models;

namespace ThreeDScan.Domain.Abstractions;

/// <summary>掃描器拍攝群組（Scan 1～16）顯示名稱持久化。</summary>
public interface IScannerProgramGroupStore
{
    /// <summary>取得該掃描器全部群組名稱；若資料不足 16 筆則自動補齊預設名稱。</summary>
    Task<IReadOnlyList<ScannerProgramGroup>> GetOrCreateAsync(string scannerId, CancellationToken ct = default);

    /// <summary>更新指定群組的顯示名稱。</summary>
    Task UpdateDisplayNameAsync(
        string scannerId,
        byte programNo,
        string displayName,
        string? updatedBy,
        CancellationToken ct = default);
}
