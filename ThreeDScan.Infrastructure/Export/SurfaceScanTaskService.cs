using System.Collections.Concurrent;
using System.Text.Json;
using ThreeDScan.Domain.Abstractions;
using ThreeDScan.Domain.Models;

namespace ThreeDScan.Infrastructure.Export;

/// <summary>面掃描任務寫在 ExportRoot/surface-tasks，供 API 重啟後仍可下載附件。</summary>
public sealed class SurfaceScanTaskService : ISurfaceScanTaskService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly ConcurrentDictionary<string, SurfaceScanTaskRecord> _cache =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly string _root;
    private readonly object _fileLock = new();

    /// <summary>以 ExportRoot/surface-tasks 建立任務檔服務。</summary>
    public SurfaceScanTaskService(IStorageSettings storage)
    {
        _root = Path.Combine(storage.ExportRoot, "surface-tasks");
        Directory.CreateDirectory(_root);
    }

    /// <inheritdoc />
    public SurfaceScanTaskRecord Create(string scannerId)
    {
        return new SurfaceScanTaskRecord
        {
            TaskId = Guid.NewGuid().ToString("N"),
            ScannerId = scannerId,
            Seq = 0,
            IsCompleted = false,
            StartedAt = DateTimeOffset.Now,
            UpdatedAt = DateTimeOffset.Now,
        };
    }

    /// <inheritdoc />
    public bool TryGet(string taskId, out SurfaceScanTaskRecord? record)
    {
        if (string.IsNullOrWhiteSpace(taskId))
        {
            record = null;
            return false;
        }

        var id = taskId.Trim();
        if (_cache.TryGetValue(id, out var cached))
        {
            record = cached;
            return true;
        }

        var path = TaskFilePath(id);
        if (!File.Exists(path))
        {
            record = null;
            return false;
        }

        lock (_fileLock)
        {
            var json = File.ReadAllText(path);
            var loaded = JsonSerializer.Deserialize<SurfaceScanTaskRecord>(json, JsonOptions);
            if (loaded is null || string.IsNullOrWhiteSpace(loaded.TaskId))
            {
                record = null;
                return false;
            }

            if (loaded.SegmentPlyPaths is null)
            {
                loaded.SegmentPlyPaths = new List<string>();
            }

            _cache[loaded.TaskId] = loaded;
            record = loaded;
            return true;
        }
    }

    /// <inheritdoc />
    public void Save(SurfaceScanTaskRecord record)
    {
        _cache[record.TaskId] = record;
        var dir = Path.Combine(_root, record.TaskId);
        Directory.CreateDirectory(dir);
        var path = TaskFilePath(record.TaskId);
        var json = JsonSerializer.Serialize(record, JsonOptions);
        lock (_fileLock)
        {
            File.WriteAllText(path, json);
        }
    }

    /// <inheritdoc />
    public string EnsureDirectory(string taskId)
    {
        var dir = Path.Combine(_root, taskId);
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <inheritdoc />
    public IReadOnlyList<SurfaceScanTaskRecord> ListIncomplete()
    {
        var result = new List<SurfaceScanTaskRecord>();
        if (!Directory.Exists(_root))
        {
            return result;
        }

        foreach (var dir in Directory.GetDirectories(_root))
        {
            var id = Path.GetFileName(dir);
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            if (!TryGet(id, out var record) || record is null || record.IsCompleted)
            {
                continue;
            }

            result.Add(record);
        }

        return result;
    }

    private string TaskFilePath(string taskId)
    {
        return Path.Combine(_root, taskId, "task.json");
    }
}
