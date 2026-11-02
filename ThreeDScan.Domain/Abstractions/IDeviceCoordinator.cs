namespace ThreeDScan.Domain.Abstractions;

/// <summary>掃描器互斥。對應 Hank <c>Scan3D.Service.DeviceCoordinator</c>。</summary>
public interface IDeviceCoordinator
{
    /// <summary>佔用掃描器；已被其他任務佔用則丟 SCANNER_BUSY。</summary>
    /// <remarks>來自 Hank Scan3D 的 <c>Scan3D.Service.DeviceCoordinator</c>。</remarks>
    /// <param name="deviceKey">裝置鍵（例如 live:ip:port）。</param>
    /// <param name="taskId">任務編號。</param>
    void Reserve(string deviceKey, string taskId);

    /// <summary>釋放該任務的佔用。</summary>
    /// <remarks>來自 Hank Scan3D 的 <c>Scan3D.Service.DeviceCoordinator</c>。</remarks>
    /// <param name="deviceKey">裝置鍵。</param>
    /// <param name="taskId">任務編號。</param>
    void Release(string deviceKey, string taskId);

    /// <summary>依任務編號釋放（找不到則略過）。</summary>
    /// <param name="taskId">任務編號。</param>
    void ReleaseByTaskId(string taskId);
}
