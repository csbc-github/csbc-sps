namespace ThreeDScan.Auth;

/// <summary>提供可用的 Access Token（含必要時自動續期／重換）。</summary>
public interface IAccessTokenSource
{
    /// <summary>取得可用 Access Token；失敗時為 <c>null</c>。</summary>
    Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default);
}
