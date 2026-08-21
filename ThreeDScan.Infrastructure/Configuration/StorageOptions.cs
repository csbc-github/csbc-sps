namespace ThreeDScan.Infrastructure.Configuration;

public sealed class StorageOptions
{
    /// <summary>預覽／Ref 根目錄，對應 appsettings <c>Storage:ContentsRoot</c>。</summary>
    public string ContentsRoot { get; set; } = "";
}
