using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace ThreeDScan.Models;

/// <summary>單一顯示點：座標與依 Z 高度著色（對齊 <c>point_cloud_viewer.prepare_point_cloud</c> 之後的結果）。</summary>
public readonly struct PointCloudRenderVertex
{
    public Point3D Position { get; init; }
    public Color Color { get; init; }

    public PointCloudRenderVertex(Point3D position, Color color)
    {
        Position = position;
        Color = color;
    }
}
