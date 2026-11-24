using System.Globalization;
using System.Security;
using System.Text;
using System.Text.Json;
using ThreeDScan.Infrastructure.LineScan.Raw;

internal static class Program
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 0 || args.Contains("--help"))
            {
                Console.WriteLine("RawProfile --input raw.ply --output NEW_DIRECTORY [--scans 20] [--allow-incomplete-recording] [--selection points.json] [--reference dimensions.json]");
                return 0;
            }
            var options = new Dictionary<string, string>(StringComparer.Ordinal);
            bool allowIncomplete = false;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--allow-incomplete-recording") { allowIncomplete = true; continue; }
                if (args[i] is not ("--input" or "--output" or "--scans" or "--selection" or "--reference")
                    || i + 1 >= args.Length || !options.TryAdd(args[i], args[++i]))
                    throw new ArgumentException("Unknown, repeated or incomplete command-line option.");
            }
            if (!options.TryGetValue("--input", out var input) || !options.TryGetValue("--output", out var output))
                throw new ArgumentException("Both --input and --output are required.");
            string directory = Path.GetFullPath(output);
            if (File.Exists(directory) || Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory).Any())
                throw new IOException("Output must be a new or empty analysis directory; existing data will not be overwritten.");
            int scans = options.TryGetValue("--scans", out var count) ? int.Parse(count, CultureInfo.InvariantCulture) : 20;
            var block = RawProfileRecordingReader.ReadFirstBlock(input, scans, allowIncomplete);
            var average = new RawProfileAverager(scans);
            foreach (var frame in block.Frames) average.AddFrame(frame.Points);
            var points = average.Result();
            bool manual = options.TryGetValue("--selection", out var selectionPath);
            IReadOnlyList<ObservedProfileMeasurement> measured;
            if (manual)
            {
                var selections = JsonSerializer.Deserialize<Selection[]>(File.ReadAllText(selectionPath!), Json)
                    ?? throw new InvalidDataException("Selection must be an array of leftRay/apexRay/rightRay objects.");
                measured = selections.Select(s => ObservedProfileDetector.Measure(points, s.LeftRay, s.ApexRay, s.RightRay)).ToArray();
            }
            else measured = ObservedProfileDetector.Detect(points);

            // Reference values are read only after detection. They never participate in
            // averaging, corner selection, coordinate generation or dimension calculation.
            Reference[] references = options.TryGetValue("--reference", out var referencePath)
                ? JsonSerializer.Deserialize<Reference[]>(File.ReadAllText(referencePath), Json)
                    ?? throw new InvalidDataException("Reference dimensions must be an array.")
                : [];
            if (references.Any(r => r.Index < 1 || new[] { r.LeftWidth, r.RightWidth, r.Height, r.Width }
                .Any(v => !double.IsFinite(v) || v <= 0)) || references.Select(r => r.Index).Distinct().Count() != references.Length)
                throw new InvalidDataException("Reference indexes must be unique and dimensions must be positive finite values.");
            var comparisons = measured.Select((m, i) =>
            {
                var reference = references.SingleOrDefault(r => r.Index == i + 1);
                return new
                {
                    index = i + 1, reference,
                    error = reference is null ? null : new
                    {
                        width = m.Width - reference.Width, height = m.Height - reference.Height,
                        leftWidth = m.LeftWidth - reference.LeftWidth, rightWidth = m.RightWidth - reference.RightWidth
                    }
                };
            }).ToArray();
            var warnings = new List<string>();
            if (!block.RecordingComplete)
                warnings.Add("INCOMPLETE RECORDING: only the available intact source frames were analyzed. This is not a completed scan result.");
            if (measured.Count == 0) warnings.Add("No complete observed angle profile was detected. Inspect the native points or supply explicit observed ray selections.");
            if (references.Length != 0 && references.Length != measured.Count)
                warnings.Add("Detected/reference counts differ; index comparisons require manual identity confirmation.");
            var report = new
            {
                method = "raw_arithmetic_mean_observed_vertices", coordinateFrame = "scanner_xy", units = "mm",
                source = block.SourcePath, recordingComplete = block.RecordingComplete,
                sourceFrames = block.Frames.Select(f => new { f.FrameId, f.Timestamp, f.Counter }),
                averageScans = average.FrameCount, pointCount = points.Count,
                calibration = false, fitting = false, extrapolation = false, dimensionCompensation = false,
                selection = manual ? "explicit_observed_ray_ids" : "observed_polyline_corners",
                warnings, measurements = measured, comparisons
            };
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "average-raw.json"), JsonSerializer.Serialize(new
            {
                coordinateFrame = "scanner_xy", averageScans = average.FrameCount,
                recordingComplete = block.RecordingComplete, points
            }, Json), Encoding.UTF8);
            File.WriteAllText(Path.Combine(directory, "measurements.json"), JsonSerializer.Serialize(report, Json), Encoding.UTF8);
            var csv = new StringBuilder("index,status,width_mm,height_mm,left_width_mm,right_width_mm,left_ray,apex_ray,right_ray\n");
            foreach (var (m, i) in measured.Select((m, i) => (m, i)))
            {
                csv.AppendLine(FormattableString.Invariant($"{i + 1},{m.Status},{m.Width:R},{m.Height:R},{m.LeftWidth:R},{m.RightWidth:R},{m.Left.Ray},{m.Apex.Ray},{m.Right.Ray}"));
                Console.WriteLine(FormattableString.Invariant($"#{i + 1} {m.Status}: width={m.Width:F3}, height={m.Height:F3}, left={m.LeftWidth:F3}, right={m.RightWidth:F3}; rays={m.Left.Ray}/{m.Apex.Ray}/{m.Right.Ray}"));
            }
            File.WriteAllText(Path.Combine(directory, "measurements.csv"), csv.ToString(), Encoding.UTF8);
            WritePlot(Path.Combine(directory, "observed-points.svg"), points, measured);
            foreach (var warning in warnings) Console.WriteLine("WARNING: " + warning);
            Console.WriteLine(directory);
            return 0;
        }
        catch (Exception error) when (error is ArgumentException or IOException or InvalidDataException or InvalidOperationException
            or JsonException or FormatException or OverflowException)
        {
            Console.Error.WriteLine(error.Message);
            return 1;
        }
    }

    private static void WritePlot(string path, IReadOnlyList<RawProfilePoint> points,
        IReadOnlyList<ObservedProfileMeasurement> measurements)
    {
        int rows = Math.Max(1, measurements.Count);
        var svg = new StringBuilder($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"1000\" height=\"{rows * 310 + 45}\" viewBox=\"0 0 1000 {rows * 310 + 45}\"><rect width=\"100%\" height=\"100%\" fill=\"white\"/><g font-family=\"Segoe UI, sans-serif\" fill=\"#172033\"><text x=\"20\" y=\"25\" font-size=\"16\">Observed scanner XY: arithmetic mean only; blue=L, red=apex, green=R</text>");
        foreach (var (m, i) in measurements.Select((m, i) => (m, i)))
        {
            double top = 45 + i * 310, xmin = Math.Min(m.Left.X, m.Right.X) - 15,
                xmax = Math.Max(m.Left.X, m.Right.X) + 15,
                ymin = new[] { m.Left.Y, m.Apex.Y, m.Right.Y }.Min() - 12,
                ymax = new[] { m.Left.Y, m.Apex.Y, m.Right.Y }.Max() + 12;
            double scale = Math.Min(920 / (xmax - xmin), 205 / (ymax - ymin));
            double X(RawProfilePoint p) => 35 + (p.X - xmin) * scale;
            double Y(RawProfilePoint p) => top + 40 + (p.Y - ymin) * scale;
            string F(double value) => value.ToString("F2", CultureInfo.InvariantCulture);
            string title = SecurityElement.Escape(FormattableString.Invariant($"#{i + 1} {m.Status} | W {m.Width:F2}, H {m.Height:F2}, L {m.LeftWidth:F2}, R {m.RightWidth:F2} mm"))!;
            svg.Append($"<text x=\"20\" y=\"{F(top + 15)}\" font-size=\"15\">{title}</text>");
            foreach (var p in points.Where(p => p.X >= xmin && p.X <= xmax && p.Y >= ymin && p.Y <= ymax))
                svg.Append($"<circle cx=\"{F(X(p))}\" cy=\"{F(Y(p))}\" r=\"1.5\" fill=\"#75808e\"/>");
            svg.Append($"<polyline points=\"{F(X(m.Left))},{F(Y(m.Left))} {F(X(m.Apex))},{F(Y(m.Apex))} {F(X(m.Right))},{F(Y(m.Right))}\" fill=\"none\" stroke=\"#b27b22\" stroke-width=\"1\"/>");
            foreach (var (p, color) in new[] { (m.Left, "#2563eb"), (m.Apex, "#dc2626"), (m.Right, "#059669") })
                svg.Append($"<circle cx=\"{F(X(p))}\" cy=\"{F(Y(p))}\" r=\"4\" fill=\"{color}\"/>");
            svg.Append($"<text x=\"20\" y=\"{F(top + 282)}\" font-size=\"12\">Observed rays {m.Left.Ray}/{m.Apex.Ray}/{m.Right.Ray}; valid samples {m.Left.SampleCount}/{m.Apex.SampleCount}/{m.Right.SampleCount}; equal XY scale</text>");
        }
        svg.Append("</g></svg>");
        File.WriteAllText(path, svg.ToString(), Encoding.UTF8);
    }

    private sealed record Selection(int LeftRay, int ApexRay, int RightRay);
    private sealed record Reference(int Index, double LeftWidth, double RightWidth, double Height, double Width);
}
