using System.Threading.Channels;
using ThreeDScan.Application.Common;

namespace ThreeDScan.Api.Services;

/// <summary>記憶體佇列；行程結束即清空。</summary>
public sealed class AcceptedWorkQueue : IAcceptedWorkQueue
{
    private readonly Channel<Func<IServiceProvider, CancellationToken, Task>> _channel =
        Channel.CreateUnbounded<Func<IServiceProvider, CancellationToken, Task>>();

    /// <inheritdoc />
    public void Enqueue(Func<IServiceProvider, CancellationToken, Task> work)
    {
        if (work is null)
        {
            throw new ArgumentNullException(nameof(work));
        }

        if (!_channel.Writer.TryWrite(work))
        {
            throw new InvalidOperationException("無法排入已接受的背景工作。");
        }
    }

    /// <inheritdoc />
    public ValueTask<Func<IServiceProvider, CancellationToken, Task>> DequeueAsync(
        CancellationToken cancellationToken)
    {
        return _channel.Reader.ReadAsync(cancellationToken);
    }
}
