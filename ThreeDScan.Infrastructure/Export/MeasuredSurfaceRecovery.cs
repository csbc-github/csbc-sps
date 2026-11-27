using System.Globalization;
using ThreeDScan.Infrastructure.Export.Models;

namespace ThreeDScan.Infrastructure.Export;

/// <summary>Grow height-selected seeds through connected, measured surface points, stopping above the ground.</summary>
internal static class MeasuredSurfaceRecovery
{
    internal static int[] Recover(
        ScanCloud source, int[] seedIndices, ScanCalibration ground, ExtractionOptions options,
        CancellationToken cancellationToken = default, bool[]? allowedMask = null)
    {
        if (allowedMask is not null && allowedMask.Length != source.Points.Length)
            throw new ArgumentException("允許點遮罩長度必須與原始點雲相同。", nameof(allowedMask));
        if (allowedMask is not null) seedIndices = seedIndices.Where(i => allowedMask[i]).ToArray();
        if (seedIndices.Length < 100) return seedIndices;
        var seedPoints = seedIndices.Select(i => source.Points[i].Position).ToArray();
        double tolerance = Math.Clamp(Math.Max(.45, ground.GroundResidualRmseMm * 2), .45, .8);
        var planes = WingPlaneSegmentation.FindPlanes(seedPoints, tolerance, 3, cancellationToken);
        if (planes.Length == 0) return seedIndices;

        double gap = MeasuredGap(source.Header);
        double normalizer = Math.Sqrt(1 + ground.GroundA * ground.GroundA + ground.GroundB * ground.GroundB);
        double groundClearance = Math.Max(.35, 3 * ground.GroundResidualRmseMm);
        bool OnSurface(ScanVector point)
        {
            foreach (var plane in planes)
                if (Math.Abs(plane.Normal.Dot(point) - plane.D) <= tolerance) return true;
            return false;
        }
        (long X, long Y, long Z) Cell(ScanVector p) =>
            ((long)Math.Floor(p.X / gap), (long)Math.Floor(p.Y / gap), (long)Math.Floor(p.Z / gap));

        // Only the discarded low band is eligible. Another component's high seeds cannot be claimed.
        var remaining = new Dictionary<(long X, long Y, long Z), List<int>>();
        for (int i = 0; i < source.Points.Length; i++)
        {
            if ((i & 65535) == 0) cancellationToken.ThrowIfCancellationRequested();
            if (allowedMask is not null && !allowedMask[i]) continue;
            var point = source.Points[i].Position;
            double height = ground.Transform(point).Z;
            if (height > options.ZThresholdMm || height / normalizer <= groundClearance || !OnSurface(point)) continue;
            var key = Cell(point);
            if (!remaining.TryGetValue(key, out var members)) remaining[key] = members = [];
            members.Add(i);
        }
        if (remaining.Count == 0) return seedIndices;

        var queue = new Queue<int>();
        foreach (int index in seedIndices)
        {
            if (allowedMask is not null && !allowedMask[index]) continue;
            var point = source.Points[index].Position;
            if (ground.Transform(point).Z <= options.ZThresholdMm + gap * normalizer * 2 && OnSurface(point))
                queue.Enqueue(index);
        }
        var recovered = new List<int>(seedIndices);
        double squaredGap = gap * gap;
        int visited = 0;
        while (queue.TryDequeue(out int index))
        {
            if ((visited++ & 65535) == 0) cancellationToken.ThrowIfCancellationRequested();
            var point = source.Points[index].Position;
            var cell = Cell(point);
            for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            for (int dz = -1; dz <= 1; dz++)
            {
                var key = (cell.X + dx, cell.Y + dy, cell.Z + dz);
                if (!remaining.TryGetValue(key, out var members)) continue;
                for (int j = members.Count - 1; j >= 0; j--)
                {
                    int candidate = members[j];
                    if (allowedMask is not null && !allowedMask[candidate]) continue;
                    var delta = source.Points[candidate].Position - point;
                    if (delta.Dot(delta) > squaredGap) continue;
                    members[j] = members[^1];
                    members.RemoveAt(members.Count - 1);
                    recovered.Add(candidate);
                    queue.Enqueue(candidate);
                }
                if (members.Count == 0) remaining.Remove(key);
            }
        }
        return recovered.Order().ToArray();
    }

    private static double MeasuredGap(string[] header)
    {
        foreach (string line in header)
        {
            if (!line.StartsWith("comment pitch_mm ", StringComparison.Ordinal)) continue;
            var fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length >= 4
                && double.TryParse(fields[2], CultureInfo.InvariantCulture, out double x)
                && double.TryParse(fields[3], CultureInfo.InvariantCulture, out double y)
                && x > 0 && y > 0 && double.IsFinite(x) && double.IsFinite(y))
                return Math.Clamp(Math.Max(x, y) * 3, 1.0, 3.0);
        }
        return 1.2;
    }
}
