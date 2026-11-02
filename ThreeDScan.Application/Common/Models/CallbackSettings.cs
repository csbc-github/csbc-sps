namespace ThreeDScan.Application.Common.Models;

/// <summary>非同步回呼重試（appsettings <c>Callback</c>；規格 4.5 可調參數）。</summary>
public sealed class CallbackSettings
{
    /// <summary>設定區段名稱。</summary>
    public const string SectionName = "Callback";

    /// <summary>最大重試次數（規格 CallbackMaxRetries，預設 5）。</summary>
    public int MaxRetries { get; set; } = 5;

    /// <summary>各次重試間隔秒數，指數退避（規格 CallbackRetryDelaySec，預設 2→4→8→16→32）。</summary>
    public int[] RetryDelaySec { get; set; } = new[] { 2, 4, 8, 16, 32 };

    /// <summary>單次 callback HTTP 逾時秒數（規格 CallbackTimeoutSec，預設 10）。</summary>
    public int TimeoutSec { get; set; } = 10;

    /// <summary>Enabled 才由 ScanCallbackWorker 送出；PollingOnly 只供查詢。</summary>
    public string Mode { get; set; } = "Enabled";

    /// <summary>已廢止：允許主機改由 SystemConfig CallbackAllowedHosts（R02／R07）。保留屬性以相容舊設定檔。</summary>
    public string[] AllowedHosts { get; set; } = Array.Empty<string>();
}
