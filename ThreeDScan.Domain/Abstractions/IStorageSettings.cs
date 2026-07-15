namespace ThreeDScan.Domain.Abstractions;

/// <summary>檔案儲存路徑設定（appsettings Storage）。</summary>
public interface IStorageSettings
{
    /// <summary>掃描影像、Ref 圖與預覽檔的根目錄。</summary>
    string ContentsRoot { get; }
}
