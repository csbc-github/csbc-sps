namespace ThreeDScan.Infrastructure.LineScan.Raw;

/// <summary>Geometric selection thresholds; none of these settings moves an input point.</summary>
public sealed record ObservedProfileOptions
{
    public double SimplificationToleranceMm { get; init; } = 3;
    public double MaximumLegDeviationMm { get; init; } = 4;
    public double MaximumAdjacentGapMm { get; init; } = 30;
    public double MinimumLegLengthMm { get; init; } = 10;
    public double MaximumLegLengthMm { get; init; } = 1000;
    public double MinimumHeightMm { get; init; } = 5;
    public int MinimumLegSamples { get; init; } = 4;
    public double MinimumAngleDegrees { get; init; } = 60;
    public double MaximumAngleDegrees { get; init; } = 120;
    public int MinimumTableSamples { get; init; } = 3;
    public double MinimumTableSupportMm { get; init; } = 6;
    public double MaximumTableDeviationMm { get; init; } = 3;
}

/// <summary>Dimensions of three observed samples in the original scanner XY frame.</summary>
public sealed record ObservedProfileMeasurement(
    RawProfilePoint Left,
    RawProfilePoint Apex,
    RawProfilePoint Right,
    double Width,
    double Height,
    double LeftWidth,
    double RightWidth,
    double AngleDegrees,
    string Status,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Selects existing mean samples using polyline turns. It performs no coordinate
/// calibration, regression, surface intersection, extrapolation or compensation.
/// </summary>
public static class ObservedProfileDetector
{
    public static IReadOnlyList<ObservedProfileMeasurement> Detect(
        IReadOnlyList<RawProfilePoint> points, ObservedProfileOptions? options = null)
    {
        var ordered = ValidatePoints(points);
        var settings = options ?? new ObservedProfileOptions();
        ValidateOptions(settings);
        var candidates = new List<ObservedProfileMeasurement>();
        int start = 0;
        for (int i = 1; i <= ordered.Length; i++)
        {
            if (i < ordered.Length && ordered[i].Ray == ordered[i - 1].Ray + 1
                && Distance(ordered[i - 1], ordered[i]) <= settings.MaximumAdjacentGapMm)
                continue;
            if (i - start >= 2 * settings.MinimumLegSamples - 1)
                DetectRun(ordered[start..i], settings, candidates);
            start = i;
        }

        // An extra turn around the same rounded apex must not create another part.
        // Prefer the candidate with table support, then the greater observed height.
        var accepted = new List<ObservedProfileMeasurement>();
        foreach (var candidate in candidates
            .OrderByDescending(c => c.Status == "Complete")
            .ThenByDescending(c => c.Height))
        {
            int low = Math.Min(candidate.Left.Ray, candidate.Right.Ray);
            int high = Math.Max(candidate.Left.Ray, candidate.Right.Ray);
            if (accepted.Any(part => low <= Math.Max(part.Left.Ray, part.Right.Ray)
                && high >= Math.Min(part.Left.Ray, part.Right.Ray)))
                continue;
            accepted.Add(candidate);
        }
        return accepted.OrderBy(c => c.Left.X).ThenBy(c => c.Apex.X).ToArray();
    }

    /// <summary>
    /// Measures explicit ray selections. Returned points are looked up in the supplied
    /// source; manual selection does not imply that table support has been verified.
    /// </summary>
    public static ObservedProfileMeasurement Measure(IReadOnlyList<RawProfilePoint> source,
        int leftRay, int apexRay, int rightRay,
        IReadOnlyList<string>? warnings = null)
    {
        var points = ValidatePoints(source).ToDictionary(point => point.Ray);
        if (leftRay == apexRay || leftRay == rightRay || apexRay == rightRay)
            throw new ArgumentException("Three distinct observed ray identifiers are required.");
        if (!points.TryGetValue(leftRay, out var left) || !points.TryGetValue(apexRay, out var apex)
            || !points.TryGetValue(rightRay, out var right))
            throw new ArgumentException("A selected ray is not present in the supplied mean samples.");
        var result = Create(left, apex, right, "Selected", warnings ?? ["手動選取原始平均點；桌面支持未經自動驗證。"]);
        if (!double.IsFinite(result.Width) || !double.IsFinite(result.Height)
            || result.Width <= 1e-9 || result.Height <= 1e-9
            || result.LeftWidth <= 1e-9 || result.RightWidth <= 1e-9)
            throw new ArgumentException("The selected samples do not form a nondegenerate observed profile.");
        return result;
    }

    private static void DetectRun(RawProfilePoint[] points, ObservedProfileOptions settings,
        List<ObservedProfileMeasurement> output)
    {
        var vertices = Simplify(points, settings.SimplificationToleranceMm);
        for (int vertex = 1; vertex + 1 < vertices.Length; vertex++)
        {
            int apexIndex = vertices[vertex];
            var apex = points[apexIndex];
            var previous = points[vertices[vertex - 1]];
            var next = points[vertices[vertex + 1]];
            double angle = Angle(previous, apex, next);
            if (!double.IsFinite(angle) || angle < settings.MinimumAngleDegrees || angle > settings.MaximumAngleDegrees
                || Distance(previous, apex) < settings.MinimumLegLengthMm
                || Distance(next, apex) < settings.MinimumLegLengthMm
                || !FacesScanner(previous, apex, next))
                continue;

            int before = FindFoot(points, vertices, apexIndex, -1, settings);
            int after = FindFoot(points, vertices, apexIndex, 1, settings);
            if (before < 0 || after < 0)
                continue;
            angle = Angle(points[before], apex, points[after]);
            double height = Deviation(points[before], points[after], apex);
            if (!double.IsFinite(angle) || angle < settings.MinimumAngleDegrees || angle > settings.MaximumAngleDegrees
                || !double.IsFinite(height) || height < settings.MinimumHeightMm)
                continue;

            bool beforeSupported = HasTableSupport(points, before, -1, points[before], points[after], settings);
            bool afterSupported = HasTableSupport(points, after, 1, points[before], points[after], settings);
            var warnings = new List<string>();
            bool leftIsBefore = points[before].X <= points[after].X;
            if (!beforeSupported)
                warnings.Add($"{(leftIsBefore ? "左" : "右")}側缺少相鄰桌面支持；保留可見斜面端點，不能視為完整腳點。");
            if (!afterSupported)
                warnings.Add($"{(leftIsBefore ? "右" : "左")}側缺少相鄰桌面支持；保留可見斜面端點，不能視為完整腳點。");
            if (new[] { points[before].SampleCount, apex.SampleCount, points[after].SampleCount }.Distinct().Count() > 1)
                warnings.Add("三個所選角點的有效平均圈數不同，請核對原始有效點數。");
            var left = leftIsBefore ? points[before] : points[after];
            var right = leftIsBefore ? points[after] : points[before];
            output.Add(Create(left, apex, right,
                beforeSupported && afterSupported ? "Complete" : "Incomplete", warnings));
        }
    }

    private static int FindFoot(RawProfilePoint[] points, int[] vertices, int apex, int direction,
        ObservedProfileOptions settings)
    {
        int best = -1;
        double greatestLength = 0;
        foreach (int candidate in vertices)
        {
            if ((candidate - apex) * direction <= 0
                || Math.Abs(candidate - apex) + 1 < settings.MinimumLegSamples)
                continue;
            double length = Distance(points[apex], points[candidate]);
            if (length < settings.MinimumLegLengthMm || length > settings.MaximumLegLengthMm || length <= greatestLength)
                continue;
            bool supported = true;
            for (int i = Math.Min(apex, candidate) + 1; i < Math.Max(apex, candidate); i++)
                if (Deviation(points[apex], points[candidate], points[i]) > settings.MaximumLegDeviationMm)
                {
                    supported = false;
                    break;
                }
            if (supported)
            {
                greatestLength = length;
                best = candidate;
            }
        }
        return best;
    }

    private static bool HasTableSupport(RawProfilePoint[] points, int foot, int direction,
        RawProfilePoint first, RawProfilePoint second, ObservedProfileOptions settings)
    {
        int count = 0;
        for (int index = foot + direction; index >= 0 && index < points.Length; index += direction)
        {
            if (Deviation(first, second, points[index]) > settings.MaximumTableDeviationMm)
                return false;
            count++;
            if (count >= settings.MinimumTableSamples
                && Distance(points[foot], points[index]) >= settings.MinimumTableSupportMm)
                return true;
        }
        return false;
    }

    private static int[] Simplify(RawProfilePoint[] points, double tolerance)
    {
        var keep = new SortedSet<int> { 0, points.Length - 1 };
        var pending = new Stack<(int First, int Last)>();
        pending.Push((0, points.Length - 1));
        while (pending.Count > 0)
        {
            var (first, last) = pending.Pop();
            int furthest = -1;
            double maximum = tolerance;
            for (int i = first + 1; i < last; i++)
            {
                double distance = Deviation(points[first], points[last], points[i]);
                if (distance > maximum)
                {
                    maximum = distance;
                    furthest = i;
                }
            }
            if (furthest < 0)
                continue;
            keep.Add(furthest);
            pending.Push((first, furthest));
            pending.Push((furthest, last));
        }
        return keep.ToArray();
    }

    private static RawProfilePoint[] ValidatePoints(IReadOnlyList<RawProfilePoint> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (points.Any(point => point is null || point.Ray is < 0 or > ushort.MaxValue || point.SampleCount < 1
            || !double.IsFinite(point.X) || !double.IsFinite(point.Y)))
            throw new ArgumentException("Observed samples require finite XY, nonnegative rays and positive sample counts.", nameof(points));
        if (points.Select(point => point.Ray).Distinct().Count() != points.Count)
            throw new ArgumentException("Observed ray identifiers must be unique.", nameof(points));
        return points.OrderBy(point => point.Ray).ToArray();
    }

    private static void ValidateOptions(ObservedProfileOptions settings)
    {
        double[] positive = [settings.SimplificationToleranceMm, settings.MaximumLegDeviationMm,
            settings.MaximumAdjacentGapMm, settings.MinimumLegLengthMm, settings.MaximumLegLengthMm,
            settings.MinimumHeightMm, settings.MinimumTableSupportMm, settings.MaximumTableDeviationMm];
        if (positive.Any(value => !double.IsFinite(value) || value <= 0)
            || settings.MinimumLegLengthMm >= settings.MaximumLegLengthMm
            || settings.MinimumLegSamples < 3 || settings.MinimumTableSamples < 2
            || !double.IsFinite(settings.MinimumAngleDegrees) || !double.IsFinite(settings.MaximumAngleDegrees)
            || settings.MinimumAngleDegrees <= 0 || settings.MinimumAngleDegrees >= settings.MaximumAngleDegrees
            || settings.MaximumAngleDegrees >= 180)
            throw new ArgumentException("Observed profile selection settings are invalid.", nameof(settings));
    }

    private static ObservedProfileMeasurement Create(RawProfilePoint left, RawProfilePoint apex,
        RawProfilePoint right, string status, IReadOnlyList<string> warnings)
        => new(left, apex, right, Distance(left, right), Deviation(left, right, apex),
            Distance(left, apex), Distance(right, apex), Angle(left, apex, right), status,
            Array.AsReadOnly(warnings.ToArray()));

    private static double Distance(RawProfilePoint a, RawProfilePoint b)
        => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    private static bool FacesScanner(RawProfilePoint first, RawProfilePoint apex, RawProfilePoint last)
    {
        // The scanner is the origin of scanner_xy. Compare sides of the observed
        // foot chord rather than radial distance: an off-axis foot may be nearer
        // to the origin even when the apex is on the visible side of the table.
        double dx = last.X - first.X, dy = last.Y - first.Y;
        double observedSide = dx * (apex.Y - first.Y) - dy * (apex.X - first.X);
        double scannerSide = dx * -first.Y - dy * -first.X;
        return double.IsFinite(observedSide) && double.IsFinite(scannerSide)
            && observedSide != 0 && scannerSide != 0 && Math.Sign(observedSide) == Math.Sign(scannerSide);
    }

    private static double Deviation(RawProfilePoint a, RawProfilePoint b, RawProfilePoint point)
    {
        double length = Distance(a, b);
        return length <= 1e-12 ? Distance(a, point)
            : Math.Abs((b.X - a.X) * (point.Y - a.Y) - (b.Y - a.Y) * (point.X - a.X)) / length;
    }

    private static double Angle(RawProfilePoint a, RawProfilePoint apex, RawProfilePoint b)
    {
        double denominator = Distance(a, apex) * Distance(b, apex);
        return denominator <= 1e-12 ? double.NaN : Math.Acos(Math.Clamp(
            ((a.X - apex.X) * (b.X - apex.X) + (a.Y - apex.Y) * (b.Y - apex.Y)) / denominator, -1, 1)) * 180 / Math.PI;
    }
}
