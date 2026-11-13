namespace ThreeDScan.Infrastructure.LineScan.Hank;

public static partial class StreamingReconstruction
{
    /// <summary>
    /// Replays the frozen capture settings while retaining the nearby table for
    /// profile fitting. Public reconstructed PLY height limits remain unchanged.
    /// </summary>
    public static IEnumerable<IReadOnlyList<Vertex>> ReadMeasurementFrames(
        string source, Calibration calibration, ReconstructionOptions settings,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        settings = settings with { MinHeight = Math.Min(settings.MinHeight, -4), RoiXY = settings.RoiXY?.ToArray() };
        Validate(calibration, settings);
        source = Path.GetFullPath(source);
        RawHeader header;
        using (var input = File.OpenRead(source)) header = Header(input);
        bool encoder = settings.PositionSource == "encoder";

        IEnumerable<MotionFrame> Frames()
        {
            var frames = encoder ? EncoderFrames(source, settings.EncoderChannel, header.Frames) : TimedFrames(source);
            foreach (var frame in frames)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return frame;
            }
        }

        long rawFrames = 0;
        double duration = 0;
        double? origin = null, countMin = null, countMax = null;
        foreach (var frame in Frames())
        {
            duration = frame.Time;
            if (frame.Points.Count > 0) rawFrames++;
            if (encoder && frame.Time >= settings.StartS && (!settings.EndS.HasValue || frame.Time <= settings.EndS.Value))
            {
                origin ??= frame.Position;
                countMin = Math.Min(countMin ?? frame.Position!.Value, frame.Position!.Value);
                countMax = Math.Max(countMax ?? frame.Position.Value, frame.Position.Value);
            }
        }

        double stop = settings.EndS ?? duration;
        if (rawFrames < 2 || stop <= settings.StartS || stop > duration + 1e-9)
            throw new ArgumentException("量測時間範圍無效或不足兩個有效截面");
        if (encoder && (!origin.HasValue || countMin == countMax))
            throw new ArgumentException("選取範圍缺少有效 Encoder 行程，不能以時間推估替代");
        double scale = 2 * Math.PI * settings.WheelRadiusMm / settings.EncoderCountsPerRev;

        foreach (var frame in Average(Frames(), settings, stop))
        {
            cancellationToken.ThrowIfCancellationRequested();
            double y = encoder
                ? settings.Direction * (frame.Position!.Value - origin!.Value) * scale
                : settings.Direction * settings.TravelMm * (frame.Time - settings.StartS) / (stop - settings.StartS);
            var points = new List<Vertex>();
            foreach (var vertex in frame.Points)
            {
                var point = vertex.XY;
                var roi = settings.RoiXY;
                if (roi != null && (point.X < roi[0] || point.X > roi[1] || point.Y < roi[2] || point.Y > roi[3])) continue;
                var local = calibration.Transform(point);
                if (local.X < settings.Xmin || local.X > settings.Xmax
                    || local.Y < settings.MinHeight || local.Y > settings.MaxHeight) continue;
                points.Add(vertex with { X = local.X, Y = y, Z = local.Y });
            }
            if (points.Count > 0) yield return points;
        }
    }
}
