using System.Collections.Concurrent;
using System.Threading.Channels;
using ThreeDScan.Application.Common;
using ThreeDScan.Application.Scan;
using ThreeDScan.Domain.Models;

namespace ThreeDScan.Api.Services;

/// <summary>面／孔背景工作佇列。對應 Hank <c>Scan3D.Service.WorkQueue</c>。</summary>
public sealed class ScanWorkQueue : IScanWorkQueue
{
    private readonly Channel<ScanWorkItem> _channel;

    /// <summary>以 SystemConfig MaximumPendingTasks 建立有界佇列。</summary>
    /// <param name="runtime">啟動載入的執行期設定。</param>
    public ScanWorkQueue(ISystemRuntimeConfig runtime)
    {
        var capacity = Math.Clamp(runtime.MaximumPendingTasks, 1, 10_000);
        _channel = Channel.CreateBounded<ScanWorkItem>(new BoundedChannelOptions(capacity)
        {
            SingleReader = true,
            FullMode = BoundedChannelFullMode.Wait,
        });
    }

    private readonly ConcurrentDictionary<string, CancellationTokenSource> _cancellations = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public CancellationToken Token(string taskId)
    {
        return _cancellations.GetOrAdd(taskId, _ => new CancellationTokenSource()).Token;
    }

    /// <inheritdoc />
    public void Cancel(string taskId)
    {
        _cancellations.GetOrAdd(taskId, _ => new CancellationTokenSource()).Cancel();
    }

    /// <inheritdoc />
    public void Finished(string taskId)
    {
        _cancellations.TryRemove(taskId, out _);
    }

    /// <inheritdoc />
    public void Enqueue(ScanWorkItem item)
    {
        if (item is null)
        {
            throw new ArgumentNullException(nameof(item));
        }

        if (!_channel.Writer.TryWrite(item))
        {
            throw new ScanTaskException(503, "QUEUE_FULL", "背景工作佇列已滿，請稍後再試。");
        }
    }

    /// <inheritdoc />
    public IAsyncEnumerable<ScanWorkItem> ReadAllAsync(CancellationToken ct)
    {
        return _channel.Reader.ReadAllAsync(ct);
    }
}
