using ThreeDScan.Domain.Models;

namespace ThreeDScan.Domain.Abstractions;

/// <summary>操作稽核日誌持久化。</summary>
public interface IOperationLogStore
{
    /// <summary>追加一筆操作記錄。</summary>
    Task AppendAsync(OperationLogEntry entry, CancellationToken ct = default);
}
