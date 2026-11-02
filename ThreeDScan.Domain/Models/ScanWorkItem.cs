namespace ThreeDScan.Domain.Models;

/// <summary>背景工作項目。對應 Hank <c>Scan3D.Service.WorkItem</c>。</summary>
public sealed class ScanWorkItem
{
    /// <summary>任務編號。</summary>
    public string TaskId { get; init; } = "";

    /// <summary>操作（例如 SurfaceComplete、HoleCapture）。</summary>
    public string Operation { get; init; } = "";
}
