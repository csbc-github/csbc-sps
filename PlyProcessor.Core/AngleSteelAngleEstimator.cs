namespace PlyProcessor.Core;

/// <summary>Estimates the smaller dihedral angle between two scanned flange surfaces.</summary>
public static class AngleSteelAngleEstimator
{
    public record Result(double AngleDegrees, double DeviationFromSquareDegrees,
        int FirstPlanePoints, int SecondPlanePoints, int TotalPoints, float InlierTolerance);

    readonly record struct Plane(double Nx, double Ny, double Nz, double D);

    public static Result? Estimate(PlyPoint[] points, float inlierTolerance = 2.5f, int iterations = 500)
    {
        if (points.Length < 100) return null;
        inlierTolerance = Math.Max(0.1f, inlierTolerance);
        var sample = points.Length <= 30_000 ? points :
            points.Where((_, i) => i % (int)Math.Ceiling(points.Length / 30_000d) == 0).ToArray();
        var random = new Random(0x41A61E);

        var first = FindPlane(sample, inlierTolerance, iterations, random);
        if (first is null) return null;
        var firstMask = points.Select(p => Distance(first.Value, p) <= inlierTolerance).ToArray();
        var remaining = points.Where((_, i) => !firstMask[i]).ToArray();
        if (remaining.Length < 50) return null;
        var second = FindPlane(remaining, inlierTolerance, iterations, random, first);
        if (second is null) return null;

        var firstPoints = points.Where((_, i) => firstMask[i]).ToArray();
        var secondPoints = remaining.Where(p => Distance(second.Value, p) <= inlierTolerance).ToArray();
        int minimumSupport = Math.Max(50, points.Length / 50);
        if (firstPoints.Length < minimumSupport || secondPoints.Length < minimumSupport) return null;

        var p1 = PcaFitter.Fit(firstPoints);
        var p2 = PcaFitter.Fit(secondPoints);
        double dot = Math.Abs(p1.E3[0] * p2.E3[0] + p1.E3[1] * p2.E3[1] + p1.E3[2] * p2.E3[2]);
        double angle = Math.Acos(Math.Clamp(dot, 0d, 1d)) * 180d / Math.PI;
        if (angle < 5d) return null;
        return new Result(angle, Math.Abs(90d - angle), firstPoints.Length,
            secondPoints.Length, points.Length, inlierTolerance);
    }

    static Plane? FindPlane(PlyPoint[] points, float tolerance, int iterations, Random random,
        Plane? nonParallelTo = null)
    {
        Plane? best = null;
        int bestCount = 0;
        for (int n = 0; n < iterations; n++)
        {
            var a = points[random.Next(points.Length)];
            var b = points[random.Next(points.Length)];
            var c = points[random.Next(points.Length)];
            double ux = b.X-a.X, uy = b.Y-a.Y, uz = b.Z-a.Z;
            double vx = c.X-a.X, vy = c.Y-a.Y, vz = c.Z-a.Z;
            double nx = uy*vz-uz*vy, ny = uz*vx-ux*vz, nz = ux*vy-uy*vx;
            double length = Math.Sqrt(nx*nx + ny*ny + nz*nz);
            if (length < 1e-6) continue;
            nx /= length; ny /= length; nz /= length;
            if (nonParallelTo is { } prior &&
                Math.Abs(nx*prior.Nx + ny*prior.Ny + nz*prior.Nz) > Math.Cos(10d * Math.PI / 180d))
                continue;
            var plane = new Plane(nx, ny, nz, -(nx*a.X + ny*a.Y + nz*a.Z));
            int count = 0;
            foreach (var p in points) if (Distance(plane, p) <= tolerance) count++;
            if (count > bestCount) { bestCount = count; best = plane; }
        }
        return bestCount >= 30 ? best : null;
    }

    static double Distance(Plane plane, PlyPoint p) =>
        Math.Abs(plane.Nx*p.X + plane.Ny*p.Y + plane.Nz*p.Z + plane.D);
}
