using System.Collections.Concurrent;
using System.Text.Json;
using ThreeDScan.Domain.Abstractions;
using ThreeDScan.Domain.Models;

namespace ThreeDScan.Infrastructure.LineScan;

/// <summary>線掃描任務寫在 ExportRoot/line-tasks。</summary>
public sealed class LineScanTaskService : ILineScanTaskService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly ConcurrentDictionary<string, LineScanTaskRecord> _cache =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly string _root;
    private readonly object _fileLock = new();

    /// <summary>以 ExportRoot/line-tasks 建立任務檔服務。</summary>
    public LineScanTaskService(IStorageSettings storage)
    {
        _root = Path.Combine(storage.ExportRoot, "line-tasks");
        Directory.CreateDirectory(_root);
    }

    /// <inheritdoc />
    public LineScanTaskRecord Create(string scannerId, double rollerSpeed, string traceId)
    {
        var now = DateTimeOffset.Now;
        var record = new LineScanTaskRecord
        {
            TaskId = Guid.NewGuid().ToString("N"),
            ScannerId = scannerId,
            RollerSpeed = rollerSpeed,
            TraceId = traceId,
            StartedAt = now,
            UpdatedAt = now,
        };
        record.SessionDirectory = Path.Combine(_root, record.TaskId);
        Directory.CreateDirectory(record.SessionDirectory);
        Save(record);
        return record;
    }

    /// <inheritdoc />
    public bool TryGet(string taskId, out LineScanTaskRecord? record)
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
            var loaded = JsonSerializer.Deserialize<LineScanTaskRecord>(File.ReadAllText(path), JsonOptions);
            if (loaded is null || string.IsNullOrWhiteSpace(loaded.TaskId))
            {
                record = null;
                return false;
            }

            _cache[loaded.TaskId] = loaded;
            record = loaded;
            return true;
        }
    }

    /// <inheritdoc />
    public bool TryGetActiveByScanner(string scannerId, out LineScanTaskRecord? record)
    {
        foreach (var task in ListIncomplete())
        {
            if (string.Equals(task.ScannerId, scannerId, StringComparison.OrdinalIgnoreCase))
            {
                record = task;
                return true;
            }
        }

        record = null;
        return false;
    }

    /// <inheritdoc />
    public void Save(LineScanTaskRecord record)
    {
        record.UpdatedAt = DateTimeOffset.Now;
        _cache[record.TaskId] = record;
        Directory.CreateDirectory(record.SessionDirectory);
        var json = JsonSerializer.Serialize(record, JsonOptions);
        lock (_fileLock)
        {
            File.WriteAllText(TaskFilePath(record.TaskId), json);
        }
    }

    /// <inheritdoc />
    public void Remove(string taskId)
    {
        if (string.IsNullOrWhiteSpace(taskId))
        {
            return;
        }

        var id = taskId.Trim();
        _cache.TryRemove(id, out _);
        var dir = Path.Combine(_root, id);
        if (Directory.Exists(dir))
        {
            Directory.Delete(dir, true);
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<LineScanTaskRecord> ListIncomplete()
    {
        var result = new List<LineScanTaskRecord>();
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
