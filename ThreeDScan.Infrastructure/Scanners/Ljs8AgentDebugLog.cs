using System.Text.Json;

namespace ThreeDScan.Infrastructure.Scanners;

internal static class Ljs8AgentDebugLog
{
    private static readonly string LogDirectory = Path.Combine(AppContext.BaseDirectory, "log");

    // #region agent log
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
