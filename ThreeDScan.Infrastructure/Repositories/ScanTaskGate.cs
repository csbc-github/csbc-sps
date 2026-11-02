namespace ThreeDScan.Infrastructure.Repositories;

/// <summary>
/// 任務寫入條帶鎖。對應 Hank <c>TaskRepository</c> 的 256 條 SemaphoreSlim。
/// </summary>
public sealed class ScanTaskGate
{
    private readonly SemaphoreSlim[] _gates;

    /// <summary>建立 256 條互斥鎖。</summary>
    public ScanTaskGate()
    {
        _gates = new SemaphoreSlim[256];
        for (var i = 0; i < _gates.Length; i++)
        {
            _gates[i] = new SemaphoreSlim(1, 1);
        }
    }

    /// <summary>依任務編號取得條帶鎖。</summary>
    /// <param name="id">任務編號。</param>
    /// <returns>該條帶的互斥鎖。</returns>
    public SemaphoreSlim For(string id)
    {
        var index = (int)((uint)id.GetHashCode() % (uint)_gates.Length);
        return _gates[index];
    }
}
