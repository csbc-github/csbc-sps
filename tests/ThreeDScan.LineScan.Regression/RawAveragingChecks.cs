using ThreeDScan.Infrastructure.LineScan.Hank;
using ThreeDScan.Infrastructure.LineScan.Raw;

namespace ThreeDScan.LineScan.Regression;

internal static class RawAveragingChecks
{
    internal static void Run()
    {
        var average = new RawProfileAverager(20);
        for (int frame = 0; frame < 20; frame++)
        {
            var source = new[]
            {
                (Ray: 400, X: 100d + frame * 30, Y: 800d - frame * 2),
                (Ray: 3, X: -200d + frame, Y: 1000d + frame * 3)
            };
            var saved = source.ToArray();
            average.AddFrame(source);
            Program.Check(source.SequenceEqual(saved), "raw averaging leaves every input frame untouched");
        }

        var result = average.Result();
        Program.Check(average.FrameCount == 20 && result.Count == 2 && result[0].Ray == 3 && result[1].Ray == 400,
            "raw averaging counts the requested 20 frames and preserves original ray identifiers in order");
        Program.Near(result[0].X, -190.5, "raw 20-frame mean X");
        Program.Near(result[0].Y, 1028.5, "raw 20-frame mean Y");
        Program.Near(result[1].X, 385, "raw averaging keeps samples spanning far more than the old 10 mm range cluster");
        Program.Near(result[1].Y, 781, "raw mean uses every valid source sample");
        Program.Check(result.All(point => point.SampleCount == 20), "raw mean records full per-ray support");
        Program.Check(average.Result().SequenceEqual(result), "raw averaging snapshots do not consume the accepted window");
        Throws<InvalidOperationException>(() => average.AddFrame(Array.Empty<(int Ray, double X, double Y)>()),
            "raw averaging does not silently accept a 21st frame into a 20-frame window");
        Program.Check(average.FrameCount == 20 && average.Result().SequenceEqual(result),
            "a full-window rejection leaves the existing result unchanged");

        var sparse = new RawProfileAverager();
        sparse.AddFrame(new[] { (Ray: 20, X: 4d, Y: 8d), (Ray: 7, X: 10d, Y: 20d) });
        sparse.AddFrame(Array.Empty<(int Ray, double X, double Y)>());
        sparse.AddFrame(new[] { (Ray: 20, X: 8d, Y: 16d) });
        var partial = sparse.Result();
        Program.Check(sparse.FrameCount == 3 && partial.Count == 2 && partial[0].Ray == 7,
            "empty and missing rays do not invent coordinates or discard a received sparse ray");
        Program.Check(partial[0].SampleCount == 1 && partial[1].SampleCount == 2,
            "raw averaging reports actual per-ray valid counts instead of an implied full window");
        Program.Near(partial[0].X, 10, "a missing ray is not averaged with a fabricated zero");
        Program.Near(partial[1].X, 6, "a sparse ray divides by its actual two source samples");
        Program.Near(partial[1].Y, 12, "sparse ray Y mean");

        var before = sparse.Result();
        void Unchanged(string reason) => Program.Check(sparse.FrameCount == 3 && sparse.Result().SequenceEqual(before), reason);
        Throws<ArgumentException>(() => sparse.AddFrame(new[] { (20, 100d, 200d), (20, 300d, 400d) }),
            "duplicate rays within one frame are rejected");
        Unchanged("duplicate-ray rejection commits no partial accumulation");
        Throws<ArgumentException>(() => sparse.AddFrame(new[] { (20, 100d, 200d), (30, double.NaN, 10d) }),
            "nonfinite XY is rejected");
        Unchanged("nonfinite rejection leaves the valid window untouched");
        Throws<ArgumentException>(() => sparse.AddFrame(new[] { (20, 100d, 200d), (30, 10d, double.PositiveInfinity) }),
            "infinite raw Y is rejected");
        Unchanged("infinite-Y rejection is atomic");
        Throws<ArgumentOutOfRangeException>(() => sparse.AddFrame(new[] { (-1, 1d, 2d) }),
            "negative ray identifiers are rejected");
        Throws<ArgumentOutOfRangeException>(() => sparse.AddFrame(new[] { (65536, 1d, 2d) }),
            "ray identifiers beyond the recorded format are rejected");
        Unchanged("invalid ray identifiers never alter the averaging window");
        Throws<InvalidOperationException>(() => sparse.AddFrame(FailingEnumeration()),
            "an interrupted source frame is rejected before commit");
        Unchanged("a failing frame enumerator cannot commit its earlier valid point");

        var overflow = new RawProfileAverager();
        overflow.AddFrame(new[] { (7, double.MaxValue, 1d) });
        Throws<OverflowException>(() => overflow.AddFrame(new[] { (20, 1d, 2d), (7, double.MaxValue, 1d) }),
            "raw accumulation overflow is rejected");
        Program.Check(overflow.FrameCount == 1 && overflow.Result().Count == 1
            && overflow.Result()[0].X == double.MaxValue && overflow.Result()[0].SampleCount == 1,
            "overflow neither commits an earlier new ray nor changes the existing ray");

        var vertices = new[]
        {
            new Vertex(11, 22, 0, 1, 1000, 2, 17, 0),
            new Vertex(33, 44, 0, 1, 1000, 2, 18, 0)
        };
        var recorded = new RawProfileAverager();
        recorded.AddFrame(vertices);
        Program.Check(recorded.Result().SequenceEqual(new[] { new RawProfilePoint(17, 11, 22, 1), new RawProfilePoint(18, 33, 44, 1) }),
            "recorded-frame overload preserves raw scanner XY without calibration");
        Throws<ArgumentException>(() => recorded.AddFrame(new[] { vertices[0], vertices[1] with { Frame = 2 } }),
            "a raw averaging frame cannot silently combine different source frames");
        Program.Check(recorded.FrameCount == 1 && recorded.Result().All(point => point.SampleCount == 1),
            "recorded-frame metadata rejection is atomic");

        var priorSnapshot = sparse.Result();
        sparse.Reset();
        Program.Check(sparse.FrameCount == 0 && sparse.Result().Count == 0 && priorSnapshot.SequenceEqual(before),
            "reset starts an empty window while earlier result snapshots remain stable");
        sparse.AddFrame(new[] { ((int)ushort.MaxValue, 1d, 2d) });
        Program.Check(sparse.FrameCount == 1 && sparse.Result()[0].Ray == ushort.MaxValue,
            "reset accepts a new independent frame and the highest supported raw ray");
        Throws<ArgumentOutOfRangeException>(() => _ = new RawProfileAverager(0), "zero-length raw windows are rejected");
        Throws<ArgumentOutOfRangeException>(() => _ = new RawProfileAverager(601), "raw windows above 600 frames are rejected");
        var limit = new RawProfileAverager();
        for (int frame = 0; frame < 600; frame++) limit.AddFrame(Array.Empty<(int Ray, double X, double Y)>());
        Throws<InvalidOperationException>(() => limit.AddFrame(Array.Empty<(int Ray, double X, double Y)>()),
            "default raw averaging window is limited to 600 frames");
        Program.Check(limit.FrameCount == 600 && limit.Result().Count == 0,
            "the frame limit retains empty scans without generating points");
        Console.WriteLine("PASS raw arithmetic averaging, actual ray support, immutable snapshots and atomic invalid-frame rejection");
    }

    private static IEnumerable<(int Ray, double X, double Y)> FailingEnumeration()
    {
        yield return (20, 100, 200);
        throw new InvalidOperationException("Simulated incomplete frame enumeration.");
    }

    private static void Throws<T>(Action action, string name) where T : Exception
    {
        try { action(); }
        catch (T) { Program.Check(true, name); return; }
        throw new InvalidOperationException(name);
    }
}
