namespace ThreeDScan.Infrastructure.Configuration;

public sealed class StorageOptions
{
    /// <summary>預覽／Ref 根目錄，對應 appsettings <c>Storage:ContentsRoot</c>。</summary>
    public string ContentsRoot { get; set; } = "";

    /// <summary>掃描 PLY／預覽匯出根目錄（相對路徑相對專案根，預設 export）。</summary>
    public string ExportRoot { get; set; } = "export";
}
