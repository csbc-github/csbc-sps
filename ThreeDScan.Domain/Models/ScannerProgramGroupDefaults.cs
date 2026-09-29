namespace ThreeDScan.Domain.Models;

/// <summary>掃描器拍攝群組（Scan 1～16）預設顯示名稱。</summary>
public static class ScannerProgramGroupDefaults
{
    /// <summary>拍攝群組數量（Scan 1～16）。</summary>
    public const int GroupCount = 16;

    private static readonly string[] OrdinalLabels =
    [
        "一", "二", "三", "四", "五", "六", "七", "八", "九", "十",
        "十一", "十二", "十三", "十四", "十五", "十六",
    ];

    /// <summary>依程式編號取得預設顯示名稱（群組一～群組十六）。</summary>
    /// <param name="programNo">0～15。</param>
    /// <returns>例如 <c>群組一</c>。</returns>
    public static string DisplayNameFor(byte programNo)
    {
        if (programNo >= GroupCount)
            throw new ArgumentOutOfRangeException(nameof(programNo));
        return $"群組{OrdinalLabels[programNo]}";
    }
}
