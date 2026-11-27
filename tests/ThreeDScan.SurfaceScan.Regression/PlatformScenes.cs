using System.Globalization;
using ThreeDScan.Infrastructure.Export;
using ThreeDScan.Infrastructure.Export.Models;

internal sealed record PlatformScene(Scene Scene, int[] ObjectIds, Dictionary<int, ScanVector> LongAxes);

internal static class PlatformScenes
{
    internal static PlatformScene Roof(bool tilted = false, bool multiple = false, bool singleWing = false)
        => Build(tilted, multiple, singleWing, false, false, false);
    internal static PlatformScene Plate(bool thinSide = false) => Build(false, false, false, true, thinSide, false);
    internal static PlatformScene UnequalFlatL(bool tilted = false) => Build(tilted, false, false, false, false, true);

    private static PlatformScene Build(bool tilted, bool multiple, bool singleWing, bool plateOnly, bool thinSide, bool flatL)
    {
        var points = new List<SourceScanPoint>(); var labels = new List<Kind>(); var ids = new List<int>();
        var n = tilted ? new ScanVector(-.18, -.11, 1).Normalize() : new ScanVector(0, 0, 1);
        var e1 = (new ScanVector(1, 0, 0) - n.Scale(n.X)).Normalize(); var e2 = n.Cross(e1).Normalize();
        var center = new ScanVector(0, 0, 43); // 70 mm above the separate floor at the origin.
        double phi = tilted ? 35 * Math.PI / 180 : 0;
        var across = e1.Scale(Math.Cos(phi)) + e2.Scale(Math.Sin(phi));
        var along = e1.Scale(-Math.Sin(phi)) + e2.Scale(Math.Cos(phi));
        var objectCenters = multiple ? new[] { -100d, 100d } : new[] { 0d };
        var axes = objectCenters.Select((_, i) => (i + 1, along)).ToDictionary(p => p.Item1, p => p.along);
        void Add(ScanVector p, Kind label, int objectId = 0)
        {
            int i = points.Count;
            points.Add(new SourceScanPoint(i, ScanPly.Quantize(p), ((i * 17) % 251).ToString(CultureInfo.InvariantCulture)));
            labels.Add(label); ids.Add(objectId);
        }
        double Noise(double a, double b) => .018 * Math.Sin(a * .17 + b * .31);
        for (int y = -230; y <= 230; y += 2)
        for (int x = -260; x <= 260; x += 2)
        {
            if (Math.Abs(x) < 205 && Math.Abs(y) < 160) continue;
            Add(new ScanVector(x, y, .02 * x - .015 * y - 27 + Noise(x, y)), Kind.Ground);
        }
        for (int b = -140; b <= 140; b++)
        for (int a = -180; a <= 180; a++)
        {
            bool covered = false;
            if (!plateOnly)
            foreach (double c in objectCenters)
            {
                double u = Math.Cos(phi) * (a - c) + Math.Sin(phi) * b;
                double v = -Math.Sin(phi) * (a - c) + Math.Cos(phi) * b;
                // Adjacent measured board cells remain directly next to the
                // object's footprint; an artificial empty moat would make XY
                // flood-fill separate the workpiece before the fix is exercised.
                covered |= flatL ? u >= 0 && u <= 90 && Math.Abs(v) <= 70
                    : u >= -40 && u <= (singleWing ? 0 : 40) && Math.Abs(v) <= 70;
            }
            if (!covered) Add(center + e1.Scale(a) + e2.Scale(b) + n.Scale(Noise(a, b)), Kind.Platform);
        }
        if (!plateOnly)
        for (int objectIndex = 0; objectIndex < objectCenters.Length; objectIndex++)
        {
            var objectCenter = center + e1.Scale(objectCenters[objectIndex]);
            for (int v = -70; v <= 70; v++)
            {
                if (flatL)
                {
                    for (int u = 0; u <= 90; u++)
                        Add(objectCenter + across.Scale(u) + along.Scale(v) + n.Scale(5 + Noise(u, v)), Kind.LowWing, objectIndex + 1);
                    for (int h = 6; h <= 55; h++)
                        Add(objectCenter + along.Scale(v) + n.Scale(h + Noise(h, v)), h <= 10 ? Kind.LowWing : Kind.Wing, objectIndex + 1);
                }
                else
                {
                    for (int u = -40; u <= (singleWing ? 0 : 40); u++)
                    {
                        double h = 42 - Math.Abs(u);
                        Add(objectCenter + across.Scale(u) + along.Scale(v) + n.Scale(h + Noise(u, v)),
                            h >= 2.5 && h <= 10.5 ? Kind.LowWing : Kind.Wing, objectIndex + 1);
                    }
                }
            }
        }
        if (thinSide)
        for (int v = -140; v <= 140; v++)
        foreach (int side in new[] { -180, 180 })
        for (int h = 0; h < 6; h++)
            Add(center + e1.Scale(side) + e2.Scale(v) - n.Scale(3 - h * .5), Kind.ThicknessSide);
        string[] header = ["ply", "format ascii 1.0", "comment pitch_mm 1 1 0.01", $"element vertex {points.Count}",
            "property float x", "property float y", "property float z", "property uchar intensity", "end_header"];
        return new PlatformScene(new Scene(new ScanCloud(header, points.ToArray()), labels.ToArray(), along), ids.ToArray(), axes);
    }
}
