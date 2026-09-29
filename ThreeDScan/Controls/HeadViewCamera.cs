using System.Windows.Media.Media3D;
using ThreeDScan.Models;

namespace ThreeDScan.Controls;

/// <summary>整支入鏡、側前方看前緣（點雲與 GLB 共用）。</summary>
public static class HeadViewCamera
{
    public static bool TryReadPoints(
        HeadFeatureDisplayInfo? features,
        out Point3D left,
        out Point3D apex,
        out Point3D right)
    {
        left = default;
        apex = default;
        right = default;
        if (features is null || string.IsNullOrEmpty(features.Kind))
        {
            return false;
        }

        left = new Point3D(features.LeftCorner.X, features.LeftCorner.Y, features.LeftCorner.Z);
        apex = new Point3D(features.Apex.X, features.Apex.Y, features.Apex.Z);
        right = new Point3D(features.RightCorner.X, features.RightCorner.Y, features.RightCorner.Z);
        return true;
    }

    public static double DistanceForDiagonal(double diagonal)
    {
        const double fovDeg = 60.0;
        var fovRad = fovDeg * Math.PI / 180.0;
        var dist = (diagonal * 0.5) / Math.Tan(fovRad / 2.0) * 1.12;
        if (dist < 1e-3 || double.IsNaN(dist))
        {
            return diagonal * 1.5;
        }

        return dist;
    }

    public static bool TryCompute(
        Point3D center,
        double diagonal,
        Point3D left,
        Point3D apex,
        Point3D right,
        out HeadViewCameraPose? pose)
    {
        pose = null;
        var head = new Point3D(
            (left.X + apex.X + right.X) / 3.0,
            (left.Y + apex.Y + right.Y) / 3.0,
            (left.Z + apex.Z + right.Z) / 3.0);
        var midLr = new Point3D(
            (left.X + right.X) * 0.5,
            (left.Y + right.Y) * 0.5,
            (left.Z + right.Z) * 0.5);

        var toBody = center - head;
        var up = apex - midLr;
        var across = right - left;
        if (toBody.LengthSquared < 1e-10)
        {
            toBody = Vector3D.CrossProduct(across, up);
        }

        if (toBody.LengthSquared < 1e-10)
        {
            return false;
        }

        toBody.Normalize();
        if (up.LengthSquared < 1e-10)
        {
            up = new Vector3D(0, 1, 0);
        }

        up.Normalize();
        up = up - toBody * Vector3D.DotProduct(up, toBody);
        if (up.LengthSquared < 1e-10)
        {
            up = new Vector3D(0, 1, 0);
            up = up - toBody * Vector3D.DotProduct(up, toBody);
            if (up.LengthSquared < 1e-10)
            {
                return false;
            }
        }

        up.Normalize();
        var camRight = Vector3D.CrossProduct(toBody, up);
        if (Vector3D.DotProduct(camRight, across) < 0)
        {
            up = -up;
        }

        up = -up;
        var dist = DistanceForDiagonal(diagonal);
        var side = Vector3D.CrossProduct(toBody, up);
        if (side.LengthSquared < 1e-12)
        {
            side = across;
        }

        if (side.LengthSquared < 1e-12)
        {
            return false;
        }

        side.Normalize();
        var eye = -toBody * 0.72 + side * 0.50 + up * 0.28;
        eye.Normalize();
        var camPos = center + eye * dist;
        pose = new HeadViewCameraPose(center, camPos, up);
        return true;
    }
}
