using System.Collections.Concurrent;
using ThreeDScan.Domain.Abstractions;
using ThreeDScan.Domain.Models;

namespace ThreeDScan.Infrastructure.Scan;

/// <summary>記憶體掃描器互斥。對應 Hank <c>Scan3D.Service.DeviceCoordinator</c>。</summary>
public sealed class DeviceCoordinator : IDeviceCoordinator
{
    private readonly ConcurrentDictionary<string, string> _owners = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public void Reserve(string deviceKey, string taskId)
    {
        var owner = _owners.GetOrAdd(deviceKey, taskId);
        if (!string.Equals(owner, taskId, StringComparison.Ordinal))
        {
            throw new ScanTaskException(409, "SCANNER_BUSY", "掃描器已有未結束任務，請先 complete 或 abort。", new { ownerTaskId = owner });
        }
    }

    /// <inheritdoc />
    public void Release(string deviceKey, string taskId)
    {
        ((ICollection<KeyValuePair<string, string>>)_owners).Remove(new KeyValuePair<string, string>(deviceKey, taskId));
    }

    /// <inheritdoc />
    public void ReleaseByTaskId(string taskId)
    {
        foreach (var pair in _owners)
        {
            if (string.Equals(pair.Value, taskId, StringComparison.Ordinal))
            {
                Release(pair.Key, taskId);
            }
        }
    }
}
