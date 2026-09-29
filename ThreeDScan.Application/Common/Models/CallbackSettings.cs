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
}
