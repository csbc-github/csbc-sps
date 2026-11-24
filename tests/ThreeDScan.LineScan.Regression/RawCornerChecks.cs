using System.IO;
using System.Text.Json;
using ThreeDScan.Infrastructure.LineScan.Raw;

namespace ThreeDScan.LineScan.Regression;

internal static class RawCornerChecks
{
    internal static void Run()
    {
        var source = Polyline((-90, 500), (-50, 500), (0, 450), (50, 500), (90, 500));
        var before = source.ToArray();
        var original = Complete(source, "observed synthetic angle");
        Program.Check(source.SequenceEqual(before), "observed detector never changes the supplied mean coordinates");
        Program.Near(original.Width, 100, "observed width uses the two input feet");
        Program.Near(original.Height, 50, "observed height is the point-to-feet-chord distance");
        Program.Near(original.LeftWidth, Math.Sqrt(5000), "observed left wing uses actual input samples");
        Program.Near(original.RightWidth, Math.Sqrt(5000), "observed right wing uses actual input samples");
        VerifyProvenance(source, original, "synthetic original");

        // The acquisition direction can reverse; provenance follows the new ray
        // sequence, while the physical left/apex/right dimensions stay the same.
        var reversed = source.Reverse().Select((point, ray) => point with { Ray = ray }).ToArray();
        var reverse = Complete(reversed, "reverse ray acquisition");
        Program.Near(reverse.Width, original.Width, "reverse acquisition preserves observed width");
        Program.Near(reverse.Height, original.Height, "reverse acquisition preserves observed height");
        Program.Near(reverse.LeftWidth, original.LeftWidth, "reverse acquisition preserves observed left wing");
        Program.Near(reverse.RightWidth, original.RightWidth, "reverse acquisition preserves observed right wing");
        VerifyProvenance(reversed, reverse, "synthetic reversed");

        // Rigidly rotating the scanner-plane coordinates changes neither lengths
        // nor chord height; no separate calibration object is supplied.
        const double angle = 7 * Math.PI / 180;
        var rotated = source.Select(point => point with
        {
            X = point.X * Math.Cos(angle) - point.Y * Math.Sin(angle),
            Y = point.X * Math.Sin(angle) + point.Y * Math.Cos(angle)
        }).ToArray();
        var turn = Complete(rotated, "rotated observed angle");
        Program.Near(turn.Width, original.Width, "scanner-plane rotation preserves observed width");
        Program.Near(turn.Height, original.Height, "scanner-plane rotation preserves observed height");
        VerifyProvenance(rotated, turn, "synthetic rotated");

        var offAxis = source.Select(point => point with { X = point.X + 2000 }).ToArray();
        var offAxisPart = Complete(offAxis, "off-axis observed angle");
        Program.Near(offAxisPart.Width, original.Width, "off-axis position preserves observed width");
        Program.Near(offAxisPart.Height, original.Height, "off-axis position preserves observed height");
        VerifyProvenance(offAxis, offAxisPart, "synthetic off-axis");

        var two = Polyline((-220, 500), (-170, 500), (-120, 450), (-70, 500),
            (80, 500), (120, 460), (160, 500), (210, 500));
        var parts = ObservedProfileDetector.Detect(two).Where(part => part.Status == "Complete")
            .OrderBy(part => part.Apex.X).ToArray();
        Program.Check(parts.Length == 2, "separate observed triangles produce two supported profiles");
        Program.Near(parts[0].Width, 100, "first supported profile width");
        Program.Near(parts[1].Width, 80, "second supported profile width");
        foreach (var part in parts) VerifyProvenance(two, part, "two supported profiles");

        var croppedLeft = Polyline((-50, 500), (0, 450), (50, 500), (90, 500));
        RequireNoComplete(croppedLeft, "cropped left support cannot be reported as a complete angle");
        var croppedRight = Polyline((-90, 500), (-50, 500), (0, 450), (50, 500));
        RequireNoComplete(croppedRight, "cropped right support cannot be reported as a complete angle");
        var missingLeg = source.Where(point => !(point.X > -50 && point.X < 0)).ToArray();
        RequireNoComplete(missingLeg, "an unsupported seventy-millimetre jump cannot become an invented complete wing");

        var repeated = Enumerable.Range(0, 12).Select(ray => new RawProfilePoint(ray, 10, 500, 20)).ToArray();
        RequireNoComplete(repeated, "coincident observed samples have no measurable triangle");
        RequireNoComplete(Array.Empty<RawProfilePoint>(), "empty observed input has no complete profile");

        var invalid = source.ToArray();
        invalid[3] = invalid[3] with { X = double.NaN };
        RejectInvalid(invalid, "nonfinite observed input must be rejected explicitly");
        invalid = source.ToArray();
        invalid[3] = invalid[3] with { Ray = invalid[2].Ray };
        RejectInvalid(invalid, "duplicate ray identities must not silently lose point provenance");
        invalid = source.ToArray();
        invalid[3] = invalid[3] with { SampleCount = 0 };
        RejectInvalid(invalid, "an averaged sample without any original observation is invalid");

        var manual = ObservedProfileDetector.Measure(source, original.Left.Ray, original.Apex.Ray, original.Right.Ray);
        Program.Check(manual.Status == "Selected" && manual.Warnings.Count > 0,
            "manual ray selections retain their unverified-support status");
        VerifyProvenance(source, manual, "manual observed ray selection");
        bool missingSelectionRejected = false;
        try { _ = ObservedProfileDetector.Measure(source, int.MaxValue, original.Apex.Ray, original.Right.Ray); }
        catch (ArgumentException) { missingSelectionRejected = true; }
        Program.Check(missingSelectionRejected, "manual selection cannot invent an unobserved ray");
        bool duplicateSelectionRejected = false;
        try { _ = ObservedProfileDetector.Measure(source, original.Left.Ray, original.Left.Ray, original.Right.Ray); }
        catch (ArgumentException) { duplicateSelectionRejected = true; }
        Program.Check(duplicateSelectionRejected, "manual selection cannot reuse one ray as two corners");

        RealObservedFixture();

        Console.WriteLine("PASS observed corner detection keeps source vertices, coherent dimensions and incomplete-data guards");
    }

    private static void RealObservedFixture()
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(Program.Fixtures, "raw-observed-20.json")));
        var source = JsonSerializer.Deserialize<RawProfilePoint[]>(fixture.RootElement.GetProperty("points").GetRawText(), Program.WebJson)!;
        Program.Check(fixture.RootElement.GetProperty("frameCount").GetInt32() == 20 && source.Length == 691,
            "captured fixture preserves all arithmetic-mean rays without application ROI or range clustering");
        var result = ObservedProfileDetector.Detect(source);
        Program.Check(result.Count == 3, "one common observed-vertex rule finds the three recorded profiles");
        Program.Check(result.Count(part => part.Status == "Complete") == 2
            && result[2].Status == "Incomplete" && result[2].Warnings.Count > 0,
            "visible third profile remains measurable but unsupported foot is explicitly incomplete");
        foreach (var part in result)
        {
            VerifyProvenance(source, part, "recorded arithmetic-mean profile");
            Program.Check(new[] { part.Left, part.Apex, part.Right }.All(point => point.SampleCount == 20),
                "recorded selected corners retain their actual twenty-frame support");
        }
    }

    private static ObservedProfileMeasurement Complete(IReadOnlyList<RawProfilePoint> points, string name)
    {
        var complete = ObservedProfileDetector.Detect(points).Where(part => part.Status == "Complete").ToArray();
        Program.Check(complete.Length == 1, name + " has exactly one supported complete profile");
        return complete[0];
    }

    private static void VerifyProvenance(IReadOnlyList<RawProfilePoint> source, ObservedProfileMeasurement part, string name)
    {
        Program.Check(source.Any(point => ReferenceEquals(point, part.Left))
            && source.Any(point => ReferenceEquals(point, part.Apex))
            && source.Any(point => ReferenceEquals(point, part.Right)),
            name + " returns existing input XY, ray identities and sample support without projected endpoints");
        Program.Check(part.Left.Ray != part.Apex.Ray && part.Apex.Ray != part.Right.Ray && part.Left.Ray != part.Right.Ray,
            name + " uses three distinct observed rays");
        static double Distance(RawProfilePoint a, RawProfilePoint b) =>
            Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));
        Program.Near(part.Width, Distance(part.Left, part.Right), name + " width agrees with returned endpoints");
        Program.Near(part.LeftWidth, Distance(part.Left, part.Apex), name + " left wing agrees with returned endpoints");
        Program.Near(part.RightWidth, Distance(part.Right, part.Apex), name + " right wing agrees with returned endpoints");
        double cross = (part.Right.X - part.Left.X) * (part.Apex.Y - part.Left.Y)
            - (part.Right.Y - part.Left.Y) * (part.Apex.X - part.Left.X);
        Program.Near(part.Height, Math.Abs(cross) / part.Width, name + " height agrees with the same observed foot chord");
    }

    private static void RequireNoComplete(IReadOnlyList<RawProfilePoint> points, string name)
    {
        var results = ObservedProfileDetector.Detect(points);
        Program.Check(results.All(part => part.Status != "Complete"), name);
        Program.Check(results.All(part => part.Status != "Incomplete" || part.Warnings.Any()),
            name + ": incomplete candidates explain their missing support");
        foreach (var part in results) VerifyProvenance(points, part, name);
    }

    private static void RejectInvalid(IReadOnlyList<RawProfilePoint> points, string name)
    {
        bool rejected = false;
        try { _ = ObservedProfileDetector.Detect(points); }
        catch (ArgumentException) { rejected = true; }
        Program.Check(rejected, name);
    }

    private static IReadOnlyList<RawProfilePoint> Polyline(params (double X, double Y)[] corners)
    {
        var points = new List<RawProfilePoint>();
        for (int i = 0; i + 1 < corners.Length; i++)
        {
            var a = corners[i]; var b = corners[i + 1];
            int samples = (int)Math.Ceiling(Math.Sqrt(Math.Pow(b.X - a.X, 2) + Math.Pow(b.Y - a.Y, 2)) / 2);
            for (int j = i == 0 ? 0 : 1; j <= samples; j++)
            {
                double ratio = j / (double)samples;
                points.Add(new RawProfilePoint(points.Count, a.X + (b.X - a.X) * ratio,
                    a.Y + (b.Y - a.Y) * ratio, 20));
            }
        }
        return points;
    }
}
