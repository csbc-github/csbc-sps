using ThreeDScan.Infrastructure.Export;
using ThreeDScan.Infrastructure.Export.Models;

/// <summary>Independently labelled broad wings exercise rim estimates and repeat captures.</summary>
internal static class WingStabilityChecks
{
    internal static void Run(Action<string, Func<object>> check)
    {
        check("broad-rounded-L-rim-and-repeatability", BroadRoundedL);
        check("broad-platform-intersection-is-not-a-wing-rim", BroadPlatform);
        check("broad-plate-six-mm-end-is-not-a-wing", ThinEnd);
        check("unmeasured-wide-wing-rim-gap-remains-unavailable", UnsupportedGap);
        check("isolated-ridge-dots-do-not-establish-longitudinal-rim", IsolatedRidgeDots);
        check("dense-upright-beats-sparse-long-side-wall", SparseSideWall);
    }

    private static object BroadRoundedL()
    {
        var variants = new List<object>();
        foreach (double yaw in new[] { 0d, 35d })
        foreach (int omitted in new[] { 0, 163 })
        {
            var fixture = Build(yaw, omitted);
            var unchanged = fixture.Points.ToArray();
            bool accepted = GeometryFitting.TryFitWings(fixture.Points,
                Enumerable.Range(0, fixture.Points.Length).ToArray(), .5, out var fit, out var reason);
            Require(accepted && fit is not null,
                $"A measured 319 mm wing with a 3.9 mm rounded rim was lost (yaw {yaw}, omitted {omitted}): {reason}");
            Require(Math.Abs(fit!.LongAxis.Dot(fixture.Axis)) > .999,
                "The broad wing's small end face replaced the independently labelled longitudinal wing.");
            Require(Math.Abs(fit.NormalAngle - 90) < 2, "The two independently generated orthogonal wings lost their angle.");
            var left = fit.Left.Indices.ToHashSet();
            var right = fit.Right.Indices.ToHashSet();
            Require(left.Count(i => fixture.Labels[i] == 1) + right.Count(i => fixture.Labels[i] == 1) > fixture.Labels.Count(x => x == 1) * .85,
                "The broad measured wing lost its independent plane support.");
            Require(Math.Max(left.Count(i => fixture.Labels[i] == 2), right.Count(i => fixture.Labels[i] == 2)) > fixture.Labels.Count(x => x == 2) * .85,
                "The upright measured wing was replaced by bend or end samples.");
            Require(fixture.Points.SequenceEqual(unchanged), "Fitting changed a measured coordinate.");
            variants.Add(new { yawDegrees = yaw, omittedInteriorSamples = omitted, pointCount = fixture.Points.Length,
                angleDegrees = fit.NormalAngle, axisAlignment = Math.Abs(fit.LongAxis.Dot(fixture.Axis)),
                leftRmsMm = fit.Left.Rms, rightRmsMm = fit.Right.Rms,
                leftRim = Rim(fit.Left), rightRim = Rim(fit.Right) });

            object Rim(PlaneFit plane)
            {
                var across = plane.Normal.Cross(fit.LongAxis).Normalize();
                int stride = Math.Max(1, plane.Indices.Length / 8192);
                var values = plane.Indices.Where((_, i) => i % stride == 0)
                    .Select(i => fixture.Points[i].Dot(across)).Order().ToArray();
                double station = fit.RidgeOrigin.Dot(across), low = values[(int)((values.Length - 1) * .01)], high = values[(int)((values.Length - 1) * .99)];
                return new { planeCount = plane.Indices.Length, trimmedRimDistanceMm = Math.Min(Math.Abs(station - low), Math.Abs(station - high)),
                    measuredRimDistanceMm = values.Min(v => Math.Abs(station - v)) };
            }
        }
        return new { name = "broad-rounded-L-rim-and-repeatability", passed = true, variants };
    }

    private static object BroadPlatform()
    {
        var points = new List<ScanVector>();
        for (int y = 0; y <= 192; y++)
        {
            for (int x = 0; x <= 319; x++) points.Add(new(x, y, Noise(x, y)));
            for (int z = 1; z <= 50; z++) points.Add(new(160 + Noise(z, y), y, z));
        }
        return Rejected("broad-platform-intersection-is-not-a-wing-rim", points.ToArray(),
            "A supporting plane crossing 159 mm past the apparent ridge became a measured wing.");
    }

    private static object ThinEnd()
    {
        var points = new List<ScanVector>();
        for (int y = 0; y <= 192; y++)
        for (int x = 0; x <= 319; x++) points.Add(new(x, y, Noise(x, y)));
        for (int x = 0; x <= 319; x++)
        for (int z = 1; z <= 12; z++) points.Add(new(x, 192 + Noise(x, z), -z * .5));
        return Rejected("broad-plate-six-mm-end-is-not-a-wing", points.ToArray(),
            "A measured 6 mm plate thickness end was called a second wing.");
    }

    private static object Rejected(string name, ScanVector[] points, string message)
    {
        bool accepted = GeometryFitting.TryFitWings(points, Enumerable.Range(0, points.Length).ToArray(), .5,
            out var fit, out var reason);
        Require(!accepted && fit is null, message);
        return new { name, passed = true, pointCount = points.Length, reason };
    }

    private static object UnsupportedGap() => Rejected("unmeasured-wide-wing-rim-gap-remains-unavailable",
        GappedWings(false), "Wings whose measured flats stop 7 mm from the common ridge passed the physical rim guard.");

    private static object IsolatedRidgeDots() => Rejected("isolated-ridge-dots-do-not-establish-longitudinal-rim",
        GappedWings(true), "A few ridge-adjacent measurements confined to one end replaced longitudinal rim support.");

    private static object SparseSideWall()
    {
        var variants = new List<object>();
        foreach (double yaw in new[] { 0d, 35d })
        {
            var fixture = Build(yaw, 0, sparseSideWall: true);
            bool accepted = GeometryFitting.TryFitWings(fixture.Points,
                Enumerable.Range(0, fixture.Points.Length).ToArray(), .5, out var fit, out var reason);
            Require(accepted && fit is not null, "Independent visible wings were lost beside a sparse long side wall: " + reason);
            Require(Math.Abs(fit!.LongAxis.Dot(fixture.Axis)) > .999,
                "The sparse 39 mm waterfall along the broad face's long side outranked the dense 50 mm upright wing.");
            Require(Math.Max(fit.Left.Indices.Count(i => fixture.Labels[i] == 2), fit.Right.Indices.Count(i => fixture.Labels[i] == 2))
                > fixture.Labels.Count(x => x == 2) * .85, "The selected second wing lacks the independently labelled upright measurements.");
            variants.Add(new { yawDegrees = yaw, pointCount = fixture.Points.Length,
                axisAlignment = Math.Abs(fit.LongAxis.Dot(fixture.Axis)), angleDegrees = fit.NormalAngle });
        }
        return new { name = "dense-upright-beats-sparse-long-side-wall", passed = true, variants };
    }

    private static ScanVector[] GappedWings(bool isolatedDots)
    {
        var points = new List<ScanVector>();
        for (int y = 0; y <= 192; y++)
        {
            for (int x = 7; x <= 326; x++) points.Add(new(x, y, Noise(x, y)));
            for (int z = 7; z <= 57; z++) points.Add(new(Noise(z, y), y, z));
        }
        if (isolatedDots)
        for (int row = 0; row < 24; row++)
        for (int column = 0; column < 8; column++)
        {
            points.Add(new(2 + column * .2, row * .25, Noise(column, row)));
            points.Add(new(Noise(column, row), row * .25, 2 + column * .2));
        }
        return points.ToArray();
    }

    private static Fixture Build(double yaw, int omitted, bool sparseSideWall = false)
    {
        var points = new List<ScanVector>(); var labels = new List<int>();
        double angle = yaw * Math.PI / 180;
        var across = new ScanVector(Math.Cos(angle), Math.Sin(angle), 0);
        var along = new ScanVector(-Math.Sin(angle), Math.Cos(angle), 0);
        var origin = new ScanVector(220, 140, 20);
        void Add(double x, double y, double z, int label)
        {
            points.Add(origin + across.Scale(x) + along.Scale(y) + new ScanVector(0, 0, z)); labels.Add(label);
        }
        int dropped = 0;
        // Dense scanner-visible broad wing. The first measured flat sample is
        // 3.9 mm from the theoretical intersection, not 1% of 319 mm farther in.
        for (int row = 0; row <= 384; row++)
        for (int column = 0; column <= 638; column++)
        {
            double x = 3.9 + column * .5, y = row * .5;
            if (dropped < omitted && row is >= 100 and <= 280 && column is >= 250 and <= 400
                && (row + column) % 13 == 0) { dropped++; continue; }
            Add(x, y, Noise(x, y), 1);
        }
        // Lower sampling density on the upright surface reflects its smaller
        // projected scanner area. Its independent 50 mm width remains measured.
        for (int row = 0; row <= 96; row++)
        for (int column = 0; column <= 100; column++)
        {
            double z = 3.9 + column * .5, y = row * 2d;
            Add(Noise(z, y), y, z, 2);
        }
        // Sparse actual measurements on a radius-3.9 bend join the two flats.
        // They are observed points, not a plane extrapolation or filled gap.
        for (int row = 0; row <= 24; row++)
        for (int arc = 1; arc < 16; arc++)
        {
            double theta = Math.PI + arc * Math.PI / 32;
            Add(3.9 + 3.9 * Math.Cos(theta), row * 8d, 3.9 + 3.9 * Math.Sin(theta), 3);
        }
        if (sparseSideWall)
        {
            // A measured sparse waterfall can have enough apparent extent to
            // form a competing pair. Its long intersection must not outweigh
            // the independently denser 50 mm longitudinal upright wing.
            for (int column = 0; column <= 159; column++)
            for (int depth = 0; depth <= 13; depth++) Add(3.9 + column * 2d, 192, -depth * 3d, 4);
        }
        else
        {
            // An observed 6 mm material end cannot stand in for the upright wing.
            for (int column = 0; column <= 638; column++)
            for (int depth = 1; depth <= 12; depth++) Add(3.9 + column * .5, 192, -depth * .5, 4);
        }
        return new Fixture(points.ToArray(), labels.ToArray(), along);
    }

    private static double Noise(double a, double b) => .44 * Math.Sin(a * 1.37 + b * .91);
    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
    private sealed record Fixture(ScanVector[] Points, int[] Labels, ScanVector Axis);
}
