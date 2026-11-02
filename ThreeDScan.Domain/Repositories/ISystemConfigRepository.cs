namespace ThreeDScan.Domain.Repositories;

/// <summary>系統鍵值設定（SystemConfig 表）。</summary>
public interface ISystemConfigRepository
{
    /// <summary>讀取鍵值；不存在回 null。</summary>
    /// <param name="key">鍵。</param>
    /// <param name="ct">取消權杖。</param>
    Task<string?> GetAsync(string key, CancellationToken ct = default);

    /// <summary>寫入或覆寫鍵值。</summary>
    /// <param name="key">鍵。</param>
    /// <param name="value">值。</param>
    /// <param name="ct">取消權杖。</param>
    Task SetAsync(string key, string value, CancellationToken ct = default);

    /// <summary>鍵不存在時寫入缺省值；已存在則不變更（R02 啟動種子）。</summary>
    /// <param name="key">鍵。</param>
    /// <param name="defaultValue">缺省值。</param>
    /// <param name="ct">取消權杖。</param>
    Task EnsureMissingAsync(string key, string defaultValue, CancellationToken ct = default);
}
