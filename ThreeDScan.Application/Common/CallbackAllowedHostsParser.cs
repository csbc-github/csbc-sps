namespace ThreeDScan.Application.Common;

/// <summary>SystemConfig CallbackAllowedHosts 字串解析（R07）。</summary>
public static class CallbackAllowedHostsParser
{
    /// <summary>
    /// 解析分號或逗號分隔的主機清單；空白字串回傳空陣列（不視為「允許全部」）。
    /// </summary>
    /// <param name="stored">資料庫原始值。</param>
    /// <returns>主機名稱陣列。</returns>
    public static string[] Parse(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored))
        {
            return Array.Empty<string>();
        }

        return stored.Split(
            new[] { ';', ',' },
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
