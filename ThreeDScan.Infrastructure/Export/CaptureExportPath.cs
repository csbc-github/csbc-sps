namespace ThreeDScan.Infrastructure.Export;

/// <summary>解析掃描匯出根目錄（專案下 export）。</summary>
public static class CaptureExportPath
{
    /// <summary>
    /// 相對路徑會往上尋找：優先 ThreeDScan.sln 所在目錄，其次 ThreeDScan.csproj；
    /// 找不到則落在 AppBaseDirectory。絕對路徑則原樣使用。
    /// </summary>
    public static string ResolveRoot(string? configuredExportRoot)
    {
        var configured = string.IsNullOrWhiteSpace(configuredExportRoot)
            ? "export"
            : configuredExportRoot.Trim();

        if (Path.IsPathRooted(configured))
            return Path.GetFullPath(configured);

        DirectoryInfo? solutionDir = null;
        DirectoryInfo? csprojDir = null;
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (solutionDir is null && File.Exists(Path.Combine(dir.FullName, "ThreeDScan.sln")))
                solutionDir = dir;
            if (csprojDir is null && File.Exists(Path.Combine(dir.FullName, "ThreeDScan.csproj")))
                csprojDir = dir;
            dir = dir.Parent;
        }

        var root = solutionDir ?? csprojDir;
        if (root is not null)
            return Path.GetFullPath(Path.Combine(root.FullName, configured));

        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, configured));
    }

    /// <summary>建立本次擷取資料夾（scannerId／時間戳／captureId 前綴）。</summary>
    public static string CreateCaptureFolder(string exportRoot, string scannerId, string captureId)
    {
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var shortId = captureId.Length >= 8 ? captureId[..8] : captureId;
        var folder = Path.Combine(exportRoot, scannerId, $"{stamp}_{shortId}");
        Directory.CreateDirectory(folder);
        return folder;
    }
}
