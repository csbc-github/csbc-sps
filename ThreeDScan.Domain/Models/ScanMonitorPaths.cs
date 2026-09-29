namespace ThreeDScan.Domain.Models;

/// <summary>
/// 監看畫面與 API 共用的最新 GLB 指標檔（寫在 ContentsRoot 下）。
/// </summary>
public static class ScanMonitorPaths
{
    /// <summary>ContentsRoot 底下放指標檔的資料夾名。</summary>
    public const string DirectoryName = "_monitor";

    /// <summary>噴印區指標檔名（相容既有路徑）。</summary>
    public const string GlbPointerFileName = "latest-glb.txt";

    /// <summary>割切區指標檔名。</summary>
    public const string CuttingGlbPointerFileName = "latest-glb-cutting.txt";

    /// <summary>最新 GLB 指標檔的絕對路徑（預設噴印區）。</summary>
    /// <param name="contentsRoot">appsettings Storage:ContentsRoot。</param>
    /// <returns>指標檔路徑；contentsRoot 空白時為空字串。</returns>
    public static string GlbPointerFile(string? contentsRoot)
    {
        return GlbPointerFile(contentsRoot, "printing");
    }

    /// <summary>依產線區域取得最新 GLB 指標檔的絕對路徑。</summary>
    /// <param name="contentsRoot">appsettings Storage:ContentsRoot。</param>
    /// <param name="machineArea">printing 或 cutting；其餘視為 printing。</param>
    /// <returns>指標檔路徑；contentsRoot 空白時為空字串。</returns>
    public static string GlbPointerFile(string? contentsRoot, string? machineArea)
    {
        if (string.IsNullOrWhiteSpace(contentsRoot))
        {
            return "";
        }

        return Path.Combine(contentsRoot.Trim(), DirectoryName, PointerFileNameForArea(machineArea));
    }

    /// <summary>該產線區域的指標檔名（FileSystemWatcher Filter 用）。</summary>
    /// <param name="machineArea">printing 或 cutting；其餘視為 printing。</param>
    /// <returns>對應的指標檔名。</returns>
    public static string PointerFileNameForArea(string? machineArea)
    {
        if (!string.IsNullOrWhiteSpace(machineArea)
            && string.Equals(machineArea.Trim(), "cutting", StringComparison.OrdinalIgnoreCase))
        {
            return CuttingGlbPointerFileName;
        }

        return GlbPointerFileName;
    }
}
