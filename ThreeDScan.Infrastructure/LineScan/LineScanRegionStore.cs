using System.Text.Json;
using ThreeDScan.Domain.Abstractions;
using ThreeDScan.Domain.Models;

namespace ThreeDScan.Infrastructure.LineScan;

/// <summary>把框選範圍寫在匯出目錄的 line-roi.json。</summary>
public sealed class LineScanRegionStore : ILineScanRegionStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly string _path;
    private readonly object _gate = new();

    /// <summary>以匯出根目錄建立框選檔。</summary>
    public LineScanRegionStore(IStorageSettings storage)
    {
        _path = Path.Combine(storage.ExportRoot, "line-roi.json");
    }

    /// <inheritdoc />
    public LineScanRegion? Get()
    {
        lock (_gate)
        {
            if (!File.Exists(_path))
            {
                return null;
            }

            var region = JsonSerializer.Deserialize<LineScanRegion>(File.ReadAllText(_path), JsonOptions);
            if (region is null || region.XMax <= region.XMin || region.YMax <= region.YMin)
            {
                return null;
            }

            return region;
        }
    }

    /// <inheritdoc />
    public void Save(LineScanRegion? region)
    {
        lock (_gate)
        {
            if (region is null || region.XMax <= region.XMin || region.YMax <= region.YMin)
            {
                if (File.Exists(_path))
                {
                    File.Delete(_path);
                }

                return;
            }

            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(_path, JsonSerializer.Serialize(region, JsonOptions));
        }
    }
}
