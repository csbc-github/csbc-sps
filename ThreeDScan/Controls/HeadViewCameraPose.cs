using System.Windows.Media.Media3D;

namespace ThreeDScan.Controls;

/// <summary>前緣預設視角：目標點、相機位置、軌道上方向。</summary>
public sealed class HeadViewCameraPose
{
    public Point3D Target { get; }

    public Point3D CameraPosition { get; }

    public Vector3D OrbitUp { get; }

    public HeadViewCameraPose(Point3D target, Point3D cameraPosition, Vector3D orbitUp)
    {
        Target = target;
        CameraPosition = cameraPosition;
        OrbitUp = orbitUp;
    }
}
