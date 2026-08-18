namespace ThreeDScan.Infrastructure.Configuration;

public sealed class StorageOptions
{
    public string ContentsRoot { get; set; } = @"D:\3DScan\contents";

    /// <summary>掃描 PLY／預覽匯出根目錄（相對路徑相對專案根，預設 export）。</summary>
    public string ExportRoot { get; set; } = "export";
}
