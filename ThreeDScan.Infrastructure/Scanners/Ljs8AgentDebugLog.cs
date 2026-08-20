using System.Text.Json;

namespace ThreeDScan.Infrastructure.Scanners;

internal static class Ljs8AgentDebugLog
{
    private const string LogPath = @"d:\Project\CSBS\角鋼\Program\ThreeDScan\debug-710818.log";

    // #region agent log
    public static void Write(string location, string message, object data, string hypothesisId)
    {
        try
        {
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
            File.AppendAllText(LogPath, line + Environment.NewLine);
        }
        catch
        {
            // ignore debug log failures
        }
    }
    // #endregion
}
