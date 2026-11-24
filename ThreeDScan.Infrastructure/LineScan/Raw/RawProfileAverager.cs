using ThreeDScan.Infrastructure.LineScan.Hank;

namespace ThreeDScan.Infrastructure.LineScan.Raw;

/// <summary>An observed scanner-XY sample, averaged only with the same original ray.</summary>
public sealed record RawProfilePoint(int Ray, double X, double Y, int SampleCount);

/// <summary>
/// Arithmetic XY means for an explicitly bounded set of raw scan frames. It does
/// not cluster ranges, require a support percentage, fill missing rays or apply
/// calibration, ROI, spatial filtering or dimension corrections.
/// </summary>
public sealed class RawProfileAverager
{
    private readonly Dictionary<int, Sum> _sums = new();

    /// <summary>Creates a window accepting between one and 600 source frames.</summary>
    public RawProfileAverager(int maximumFrames = 600)
    {
        if (maximumFrames is < 1 or > 600)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumFrames), "Raw averaging accepts 1 to 600 frames.");
        }

        MaximumFrames = maximumFrames;
    }

    /// <summary>The maximum accepted frames before an explicit reset.</summary>
    public int MaximumFrames { get; }

    /// <summary>Accepted source frames, including frames without valid points.</summary>
    public int FrameCount { get; private set; }

    /// <summary>
    /// Adds one frame without changing any source point. All validation and
    /// overflow checks finish before the window is changed.
    /// </summary>
    public void AddFrame(IEnumerable<(int Ray, double X, double Y)> frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (FrameCount == MaximumFrames)
        {
            throw new InvalidOperationException("The raw averaging window is full; reset it before adding another frame.");
        }

        var pending = new Dictionary<int, Sum>();
        foreach (var (ray, x, y) in frame)
        {
            if (ray is < 0 or > ushort.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(frame), "Raw ray identifiers must fit the recorded unsigned 16-bit index.");
            }

            if (!double.IsFinite(x) || !double.IsFinite(y))
            {
                throw new ArgumentException("Raw XY coordinates must be finite.", nameof(frame));
            }

            if (pending.ContainsKey(ray))
            {
                throw new ArgumentException("A source frame cannot contain the same ray more than once.", nameof(frame));
            }

            _sums.TryGetValue(ray, out var previous);
            double sumX = previous.X + x;
            double sumY = previous.Y + y;
            if (!double.IsFinite(sumX) || !double.IsFinite(sumY))
            {
                throw new OverflowException("Raw XY accumulation exceeded the finite coordinate range.");
            }

            pending.Add(ray, new(sumX, sumY, checked(previous.Count + 1)));
        }

        // Enumeration itself may fail, so commit only after the whole frame was read.
        foreach (var (ray, sum) in pending)
        {
            _sums[ray] = sum;
        }

        FrameCount++;
    }

    /// <summary>Adds the scanner XY from exactly one recorded raw frame.</summary>
    public void AddFrame(IEnumerable<Vertex> frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        AddFrame(ReadFrame());

        IEnumerable<(int Ray, double X, double Y)> ReadFrame()
        {
            Vertex? first = null;
            foreach (var vertex in frame)
            {
                if (vertex is null)
                {
                    throw new ArgumentException("A raw frame cannot contain a null vertex.", nameof(frame));
                }

                first ??= vertex;
                if (vertex.Frame != first.Frame || vertex.Timestamp != first.Timestamp || vertex.Counter != first.Counter)
                {
                    throw new ArgumentException("Raw vertices must belong to the same recorded frame, timestamp and scan counter.", nameof(frame));
                }

                yield return (vertex.Ray, vertex.X, vertex.Y);
            }
        }
    }

    /// <summary>
    /// Returns a ray-sorted snapshot; each mean uses its own actual valid sample
    /// count, rather than the total frame count. Reading does not drain the window.
    /// </summary>
    public IReadOnlyList<RawProfilePoint> Result()
    {
        var result = _sums.OrderBy(pair => pair.Key).Select(pair =>
            new RawProfilePoint(pair.Key, pair.Value.X / pair.Value.Count,
                pair.Value.Y / pair.Value.Count, pair.Value.Count)).ToArray();
        return Array.AsReadOnly(result);
    }

    /// <summary>Starts a new independent averaging window.</summary>
    public void Reset()
    {
        _sums.Clear();
        FrameCount = 0;
    }

    private readonly record struct Sum(double X, double Y, int Count);
}
