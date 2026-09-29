using Keyence.LjDevCommunication;

namespace ThreeDScan.Infrastructure.Scanners;

/// <summary>
/// LJ Developer DLL 的全域初始化管理。
/// 依手冊規定 Initialize 整個應用程式只能執行一次，且不可在每次操作後 Finalize，
/// 否則 DLL 內部連線狀態會被破壞，導致後續 GetActiveProgram 回 NotOpen(0x1001)。
/// </summary>
internal static class Ljs8Runtime
{
    private static readonly object Gate = new();
    private static bool _initialized;

    /// <summary>
    /// LJ Developer DLL 為行程全域（device id 固定 0），不可並行存取。
    /// 任何開啟連線到關閉連線的操作都必須先取得此鎖，避免兩個操作互相關閉對方的連線
    /// （症狀：連線中途出現 NotOpen(0x1001)）。
    /// </summary>
    private static readonly SemaphoreSlim DeviceGate = new(1, 1);

    /// <summary>行程內只初始化 LJ DLL 一次。</summary>
    public static void EnsureInitialized()
    {
        if (_initialized) return;
        lock (Gate)
        {
            if (_initialized) return;
            Ljs8If.Initialize();
            _initialized = true;
        }
    }

    /// <summary>取得全域裝置鎖。</summary>
    public static void AcquireDevice()
    {
        DeviceGate.Wait();
    }

    /// <summary>釋放全域裝置鎖。</summary>
    public static void ReleaseDevice()
    {
        DeviceGate.Release();
    }
}
