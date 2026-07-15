namespace ThreeDScan.Domain;

/// <summary>掃描器拍攝群組（Scan 1～16）預設顯示名稱。</summary>
public static class ScannerProgramGroupDefaults
{
    public const int GroupCount = 16;

    private static readonly string[] OrdinalLabels =
    [
        "一", "二", "三", "四", "五", "六", "七", "八", "九", "十",
        "十一", "十二", "十三", "十四", "十五", "十六",
    ];

    public static string DisplayNameFor(byte programNo)
    {
        if (programNo >= GroupCount)
            throw new ArgumentOutOfRangeException(nameof(programNo));
        return $"群組{OrdinalLabels[programNo]}";
    }
}
