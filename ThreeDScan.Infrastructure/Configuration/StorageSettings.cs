using Microsoft.Extensions.Options;
using ThreeDScan.Domain.Abstractions;
using ThreeDScan.Infrastructure.Export;

namespace ThreeDScan.Infrastructure.Configuration;

/// <summary>檔案儲存路徑設定（appsettings Storage 區段）。</summary>
public sealed class StorageSettings : IStorageSettings
{
    private readonly StorageOptions _options;
    private readonly string _exportRoot;

    /// <summary>讀取 Storage 設定並解析匯出根目錄。</summary>
    public StorageSettings(IOptions<StorageOptions> options)
    {
        _options = options.Value;
        _exportRoot = CaptureExportPath.ResolveRoot(_options.ExportRoot);
    }

    /// <inheritdoc />
    public string ContentsRoot
    {
        get
        {
            return _options.ContentsRoot;
        }
    }

    /// <inheritdoc />
    public string ExportRoot
    {
        get
        {
            return _exportRoot;
        }
    }
}
