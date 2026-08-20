using Microsoft.Extensions.Options;
using ThreeDScan.Domain.Abstractions;

namespace ThreeDScan.Infrastructure.Configuration;

/// <summary>檔案儲存路徑設定（appsettings Storage 區段）。</summary>
public sealed class StorageSettings : IStorageSettings
{
    private readonly StorageOptions _options;

    public StorageSettings(IOptions<StorageOptions> options)
    {
        _options = options.Value;
    }

    /// <inheritdoc />
    public string ContentsRoot
    {
        get
        {
            return _options.ContentsRoot;
        }
    }
}
