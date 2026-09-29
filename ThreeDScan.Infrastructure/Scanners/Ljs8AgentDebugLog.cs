using System.Text.Json;

namespace ThreeDScan.Infrastructure.Scanners;

/// <summary>寫入本機 debug JSON 行，供現場排查 LJ 設定。</summary>
internal static class Ljs8AgentDebugLog
{
    private static readonly string LogDirectory = Path.Combine(AppContext.BaseDirectory, "log");

    // #region agent log
    /// <summary>寫入除錯 JSON 行（失敗忽略）。</summary>
    public static void Write(string location, string message, object data, string hypothesisId)
    {
        try
        {
            Directory.CreateDirectory(LogDirectory);
            var line = JsonSerializer.Serialize(new
            {
                sessionId = "710818",
                runId = "post-fix",
                hypothesisId,
                location,
                message,
                data,
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            });
            var fileName = "debug-" + DateTime.Now.ToString("yyyyMMdd") + ".log";
            File.AppendAllText(Path.Combine(LogDirectory, fileName), line + Environment.NewLine);
        }
        catch
        {
            // ignore debug log failures
        }
    }
    // #endregion
}
