using ThreeDScan.Infrastructure.Export;
using ThreeDScan.Infrastructure.Export.Models;

internal static class SparseEdgeChecks
{
    // The factory is supplied by the test runner so these independent scenes
    // exercise the production edge estimator without copying its algorithm.
    internal static void Run(Action<string, Func<object>> run,
        Func<ScanVector[], IEnumerable<int>, double, ProfilePoint[]> edgeProfile)
    {
        run("sparse-longitudinal-tail-does-not-define-wing-edge", () =>
        {
            var first = Band(-150, 150, 40, 40).Concat(Tail(-156, -150, .1)).ToArray();
            var second = Band(-150, 150, 40, 40).Concat(Tail(150, 154, 9.6)).ToArray();
            var a = Profile(first); var b = Profile(second);
            Require(a[0].U >= -150 && a[^1].U <= 150 && b[0].U >= -150 && b[^1].U <= 150,
                "Three adjacent longitudinal samples outside the labelled body must not define a full-section edge.");
            var check = StitchMath.CheckEdges(a, b, new());
            Require(check.Passed, "Matching measured bodies must remain compatible despite separately located sparse on-plane tails.");
            return new { firstPoints = first.Length, secondPoints = second.Length, check };
        });

        run("complete-low-density-wing-retains-physical-outer-edge", () =>
        {
            // Every left-wing column has only seven measurements, spread across
            // the complete 10 mm longitudinal band. The right wing has sixty.
            // A fixed minimum count or whole-profile density threshold would
            // wrongly erase the independently complete left wing.
            var lowDensity = Profile(Band(-150, 150, 7, 60));
            var dense = Profile(Band(-150, 150, 45, 45));
            Require(Math.Abs(lowDensity[0].U + 149.75) < .51 && Math.Abs(lowDensity[^1].U - 149.75) < .51,
                "A consistently measured low-density wing must retain its independently defined full width.");
            var check = StitchMath.CheckEdges(lowDensity, dense, new());
            Require(check.Passed, "A sampling-density imbalance must not move an otherwise identical measured edge.");
            return new { leftSamplesPerColumn = 7, rightSamplesPerColumn = 60, check };
        });

        run("consistently-missing-3mm-edge-still-fails", () => MissingEdge(3));
        run("consistently-missing-15mm-edge-still-fails", () => MissingEdge(15));

        run("matching-incomplete-ends-still-fail-independent-interior-width", () =>
        {
            var endA = Profile(Band(-147, 150, 35, 35));
            var endB = Profile(Band(-147, 150, 12, 45));
            var interior = Profile(Band(-150, 150, 50, 50));
            var agreement = StitchMath.CheckEdges(endA, endB, new());
            Require(agreement.Passed, "The independently identical incomplete ends should agree with one another.");
            var coverageA = StitchMath.CheckWidthCoverage(interior, endA, new());
            var coverageB = StitchMath.CheckWidthCoverage(interior, endB, new());
            Require(!coverageA.Passed && !coverageB.Passed
                && coverageA.LeftEdgeDistanceMm > 1.5 && coverageB.LeftEdgeDistanceMm > 1.5,
                "Matching incomplete end scans cannot substitute for full width supported by their independent interiors.");
            return new { observedMissingMm = 3, agreement, coverageA, coverageB };
        });

        run("sparse-but-longitudinally-repeated-outer-edge-is-retained", () =>
        {
            var profile = Profile(Band(-150, 150, 3, 45));
            Require(Math.Abs(profile[0].U + 149.75) < .51 && Math.Abs(profile[^1].U - 149.75) < .51,
                "Three spatially separated measurements per column must not be confused with three clustered tail samples.");
            var check = StitchMath.CheckEdges(profile, Profile(Band(-150, 150, 35, 35)), new());
            Require(check.Passed, "A sparse wing repeated throughout the band must retain its complete physical edge.");
            return new { leftSamplesPerColumn = 3, check };
        });

        run("isolated-dense-column-in-sparse-tail-does-not-define-edge", () =>
        {
            // The tail has a large longitudinal span but only three occupied
            // stations per column. One column is fully populated; that single
            // transverse spike must not acquire the status of a persistent
            // two-dimensional wing boundary.
            var body = Band(-150, 150, 40, 40);
            var tail = Band(-156, -150, 3, 3);
            var spike = Enumerable.Range(0, 40).Select(row =>
                new ScanVector(-153.75, .1 + 9.8 * row / 39, -153.75));
            var edge = Profile(body.Concat(tail).Concat(spike).ToArray());
            Require(edge[0].U >= -150,
                "A single high-occupancy transverse column among weak tail columns must not move the physical edge.");
            var check = StitchMath.CheckEdges(edge, Profile(body), new());
            Require(check.Passed, "The independently unchanged body must remain compatible after an isolated occupancy spike.");
            return new { spikeU = -153.75, check };
        });

        run("one-mm-raster-does-not-require-unmeasured-half-mm-columns", () =>
        {
            var points = new List<ScanVector>();
            for (double x = -149.5; x < 150; x += 1)
                for (int row = 0; row < 20; row++)
                    points.Add(new ScanVector(x, .1 + 9.8 * row / 19, -Math.Abs(x)));
            var edge = Profile(points.ToArray());
            Require(edge.Length == 300 && Math.Abs(edge[0].U + 149.5) < 1e-9
                && Math.Abs(edge[^1].U - 149.5) < 1e-9,
                "A complete 1mm measurement raster must preserve both observed endpoints despite empty alternating 0.5mm bins.");
            var check = StitchMath.CheckEdges(edge, edge, new());
            Require(check.Passed, "A repeated complete 1mm raster must pass its original edge and coverage checks.");
            return new { measuredColumnCount = 300, check };
        });

        ProfilePoint[] Profile(ScanVector[] points) => edgeProfile(points, Enumerable.Range(0, points.Length), .5);

        object MissingEdge(double missing)
        {
            var complete = Profile(Band(-150, 150, 40, 40));
            var incomplete = Profile(Band(-150 + missing, 150, 40, 40));
            var check = StitchMath.CheckEdges(complete, incomplete, new());
            Require(!check.Passed && check.LeftEdgeDistanceMm > 1.5,
                $"A consistent {missing} mm loss of measured wing width must still fail the original 1.5 mm edge gate.");
            if (missing == 3)
                Require(check.CommonCoverageRatio > .98,
                    "The small missing-edge fixture must isolate the edge-distance gate while common coverage remains acceptable.");
            return new { independentlyRemovedMm = missing, check };
        }
    }

    // Coordinates are an independently known 90-degree roof in the local
    // section frame. X spans are explicit physical labels, never inferred from
    // production fitting or its edge estimator.
    private static ScanVector[] Band(double minimumX, double maximumX, int leftRows, int rightRows)
    {
        var points = new List<ScanVector>();
        for (double x = minimumX + .25; x < maximumX; x += .5)
        {
            int rows = x < 0 ? leftRows : rightRows;
            for (int row = 0; row < rows; row++)
            {
                double y = .1 + 9.8 * row / Math.Max(1, rows - 1);
                points.Add(new ScanVector(x, y, -Math.Abs(x)));
            }
        }
        return points.ToArray();
    }

    private static IEnumerable<ScanVector> Tail(double minimumX, double maximumX, double firstY)
    {
        for (double x = minimumX + .25; x < maximumX; x += .5)
            for (int sample = 0; sample < 3; sample++)
                yield return new ScanVector(x, firstY + sample * .1, -Math.Abs(x));
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
