using System.Diagnostics;
using System.IO;

namespace ThreeDScan.Auth;

/// <summary>
/// Client Credentials 診斷 Log：同時寫入 VS「輸出」視窗與執行目錄檔案。
/// 檔案：<c>{BaseDirectory}/auth-client-credentials.log</c>
/// </summary>
internal static class AuthDebugLog
{
    private static readonly object Gate = new();
    private static readonly string LogPath =
        Path.Combine(AppContext.BaseDirectory, "auth-client-credentials.log");

    public static string FilePath => LogPath;

    public static void Write(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
        Debug.WriteLine("[Auth] " + line);
        try
        {
            lock (Gate)
            {
                File.AppendAllText(LogPath, line + Environment.NewLine);
            }
        }
        catch
        {
            // 忽略寫檔失敗，至少 Debug 有輸出
        }
    }

    public static void Clear()
    {
        try
        {
            if (File.Exists(LogPath))
                File.Delete(LogPath);
        }
        catch
        {
            // ignore
        }
    }
}
