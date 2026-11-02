namespace ThreeDScan.Infrastructure.Export.Models;

/// <summary>
/// PLY 平面原點。對應 Hank <c>Scan3D.Scanner.PointCloudOrigin</c>。
/// </summary>
public enum PointCloudOrigin
{
    /// <summary>使用 SDK XStart／YStart 當平面原點。</summary>
    Scanner = 0,

    /// <summary>平面原點歸零（區域格網）。</summary>
    LocalGrid = 1,
}
