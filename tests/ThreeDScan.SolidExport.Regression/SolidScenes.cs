using ThreeDScan.Infrastructure.Export.Models;

internal sealed record SolidScene(ScanVector[] Points, Func<ScanVector, ScanVector> ToLocal,
    Func<ScanVector, ScanVector> RotateNormal, bool FlatL, bool Hole);

internal static class SolidScenes
{
    internal static SolidScene Roof(double yaw, double pitch = 0, double roll = 0, bool hole = false, bool twist = false)
        => Build(yaw, pitch, roll, false, hole, twist);
    internal static SolidScene FlatL() => Build(35, 12, 27, true, false, false);

    internal static SolidScene RoofWithOffPlaneTailIsland()
    {
        var scene = Roof(79.6, 17, 31, hole: true);
        // The first analytic point is (-40,-70,-40). Add an independent small
        // measured patch 20 mm beyond the true tail and off both wing planes.
        var reference = scene.Points[0];
        var points = scene.Points.ToList();
        for (int y = 90; y <= 100; y++)
            for (int x = -5; x <= 5; x++)
                points.Add(reference + scene.RotateNormal(new(x + 40, y + 70, 15 + 40)));
        return scene with { Points = points.ToArray() };
    }

    internal static SolidScene RoofWithMeasuredRoundedRidge()
    {
        var scene = Roof(79.6, 17, 31, hole: true);
        const double radius = 6;
        var points = scene.Points.Select(world =>
        {
            var local = scene.ToLocal(world);
            if (Math.Abs(local.X) >= radius / Math.Sqrt(2)) return world;
            // Independently constructed circular outer bend tangent to the two
            // 45-degree wings. These actual curved samples are off their flat
            // extensions, but their observed footprint still joins both wings.
            double z = -radius * Math.Sqrt(2) + Math.Sqrt(radius * radius - local.X * local.X);
            return world + scene.RotateNormal(new(0, 0, z - local.Z));
        }).ToArray();
        return scene with { Points = points };
    }

    private static SolidScene Build(double yawDegrees, double pitchDegrees, double rollDegrees, bool flatL, bool hole, bool twist)
    {
        double yaw=yawDegrees*Math.PI/180,pitch=pitchDegrees*Math.PI/180,roll=rollDegrees*Math.PI/180;
        ScanVector Rotate(ScanVector p)
        {
            // Full independent Rx(pitch), Ry(roll), Rz(yaw), not a projected A/C pose.
            var q=new ScanVector(p.X,Math.Cos(pitch)*p.Y-Math.Sin(pitch)*p.Z,Math.Sin(pitch)*p.Y+Math.Cos(pitch)*p.Z);
            var r=new ScanVector(Math.Cos(roll)*q.X+Math.Sin(roll)*q.Z,q.Y,-Math.Sin(roll)*q.X+Math.Cos(roll)*q.Z);
            return new ScanVector(Math.Cos(yaw)*r.X-Math.Sin(yaw)*r.Y,Math.Sin(yaw)*r.X+Math.Cos(yaw)*r.Y,r.Z);
        }
        var ex=Rotate(new(1,0,0));var ey=Rotate(new(0,1,0));var ez=Rotate(new(0,0,1));
        var origin=new ScanVector(371.3,451.7,62.9);
        ScanVector ToLocal(ScanVector world){var p=world-origin;return new(p.Dot(ex),p.Dot(ey),p.Dot(ez));}
        var points=new List<ScanVector>();
        for(int v=-70;v<=70;v++)
        {
            if(flatL)
            {
                for(int u=0;u<=90;u++)points.Add(origin+Rotate(new(u,v,0)));
                for(int h=1;h<=50;h++)points.Add(origin+Rotate(new(0,v,h)));
            }
            else for(int u=-40;u<=40;u++)
            {
                if(hole&&(u+22)*(u+22)+(v-15)*(v-15)<=49)continue;
                var p=new ScanVector(u,v,-Math.Abs(u));
                if(twist)
                {
                    double angle=20*Math.PI/180*v/70;
                    p=new(Math.Cos(angle)*p.X+Math.Sin(angle)*p.Z,p.Y,-Math.Sin(angle)*p.X+Math.Cos(angle)*p.Z);
                }
                points.Add(origin+Rotate(p));
            }
        }
        return new(points.ToArray(),ToLocal,Rotate,flatL,hole);
    }
}
