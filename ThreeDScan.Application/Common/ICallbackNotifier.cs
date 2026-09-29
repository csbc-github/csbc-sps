using ThreeDScan.Application.Scan.Models;

namespace ThreeDScan.Application.Common;

/// <summary>將任務結果 POST 至台船 callbackUrl。</summary>
public interface ICallbackNotifier
{
    /// <summary>依 Callback 設定重試；全部失敗仍保留任務庫結果。</summary>
    Task NotifyAsync(string callbackUrl, TaskResultData result, CancellationToken ct);
}
