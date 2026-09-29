namespace ThreeDScan.Application.Common;

/// <summary>將已接受（202）的後續工作排入背景執行。</summary>
public interface IAcceptedWorkQueue
{
    /// <summary>排入工作；呼叫端不得捕捉 Scoped 服務。</summary>
    void Enqueue(Func<IServiceProvider, CancellationToken, Task> work);

    /// <summary>取出下一筆工作；取消時結束等待。</summary>
    ValueTask<Func<IServiceProvider, CancellationToken, Task>> DequeueAsync(CancellationToken cancellationToken);
}
