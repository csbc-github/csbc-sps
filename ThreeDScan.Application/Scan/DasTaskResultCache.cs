using System.Collections.Concurrent;
using ThreeDScan.Application.Scan.Models;

namespace ThreeDScan.Application.Scan;

/// <summary>程序內任務結果快取；重啟後清空。</summary>
public sealed class DasTaskResultCache : IDasTaskResultCache
{
    private readonly ConcurrentDictionary<string, TaskResultData> _items =
        new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public void Set(TaskResultData data)
    {
        if (data is null || string.IsNullOrWhiteSpace(data.TaskId))
        {
            return;
        }

        _items[data.TaskId.Trim()] = data;
    }

    /// <inheritdoc />
    public bool TryGet(string taskId, out TaskResultData? data)
    {
        if (string.IsNullOrWhiteSpace(taskId))
        {
            data = null;
            return false;
        }

        return _items.TryGetValue(taskId.Trim(), out data);
    }
}
