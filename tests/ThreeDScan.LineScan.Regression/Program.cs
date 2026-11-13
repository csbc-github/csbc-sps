using System.IO;
using System.Reflection;
using System.Text.Json;
using ThreeDScan.Application.Scan;
using ThreeDScan.Application.Scan.Models;
using ThreeDScan.Domain.Models;
using ThreeDScan.Infrastructure.LineScan;
using ThreeDScan.Infrastructure.LineScan.Hank;

namespace ThreeDScan.LineScan.Regression;

internal static class Program
{
    internal static int Checks;
    internal static readonly string Fixtures = Path.Combine(AppContext.BaseDirectory, "Fixtures");
    internal static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    static async Task<int> Main()
    {
        try
        {
            ProtocolChecks();
            AveragingChecks();
            var measured = ProfileAndServiceChecks();
            await ContextChecks.RunAsync();
            await NoMotionChecks.RunAsync();
            await EncoderSimulationChecks.RunAsync();
            await EncoderSimulationHostChecks.RunAsync();
            await HttpChecks.RunAsync(measured);
            Console.WriteLine($"PASS {Checks} checks: protocol, averaging, 7 profiles and mirrors, active 3D measurement, zero-motion task failure, API mapping and loopback HTTP.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    internal static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException(name);
        Checks++;
    }

    internal static void Near(double actual, double expected, string name, double tolerance = 1e-7)
    {
        Check(double.IsFinite(actual) && Math.Abs(actual - expected) <= tolerance,
            $"{name}: actual={actual:R}, expected={expected:R}");
    }

    static P Point(JsonElement value) => new(value[0].GetDouble(), value[1].GetDouble());

    static void ProtocolChecks()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(Fixtures, "protocol-python-reference.json")));
        Check(document.RootElement.GetArrayLength() == 103, "103 independent Python wire cases");
        foreach (var expected in document.RootElement.EnumerateArray())
        {
            string name = expected.GetProperty("name").GetString()!;
            var payload = Convert.FromBase64String(expected.GetProperty("payload_base64").GetString()!);
            var scan = Protocol.Parse(payload);
            Check(scan.Counter == expected.GetProperty("counter").GetUInt16(), name + " counter");
            Check(scan.Timestamp == expected.GetProperty("timestamp").GetUInt32(), name + " timestamp");
            Near(scan.Frequency, expected.GetProperty("frequency").GetDouble(), name + " frequency");
            var xy = expected.GetProperty("xy");
            Check(scan.Points.Length == xy.GetArrayLength(), name + " ray count");
            for (int ray = 0; ray < scan.Points.Length; ray++)
            {
                if (xy[ray].ValueKind == JsonValueKind.Null)
                    Check(scan.Points[ray] == null, name + " invalid ray " + ray);
                else
                {
                    Check(scan.Points[ray] is P, name + " valid ray " + ray);
                    Near(scan.Points[ray]!.Value.Distance(Point(xy[ray])), 0, name + " XY " + ray);
                }
            }
            var decoder = new Protocol.Decoder();
            var framed = Protocol.Frame(payload);
            Check(decoder.Feed(framed[..5], 5).Count == 0, name + " fragmented header");
            var completed = decoder.Feed(framed[5..], framed.Length - 5);
            Check(completed.Count == 1 && completed[0].SequenceEqual(payload), name + " fragmented frame");
        }
        Console.WriteLine("PASS 103 Python wire-reference cases");
    }

    static void AveragingChecks()
    {
        var mean = new PointMean();
        var averagerType = typeof(LineScanMeasureService).Assembly.GetType("ThreeDScan.Infrastructure.LineScan.LineScanFrameAverager")!;
        var duplicate = Activator.CreateInstance(averagerType)!;
        var add = averagerType.GetMethod("Add")!;
        for (int frame = 0; frame < 20; frame++)
        {
            double radius = frame < 10 ? 100 : 110;
            var rays = Enumerable.Range(0, 3600).Select(ray =>
            {
                double angle = ray / 10.0 * Math.PI / 180;
                return (P?)new P(radius * Math.Cos(angle), radius * Math.Sin(angle));
            }).ToArray();
            mean.Add(rays);
            add.Invoke(duplicate, [rays.Select((point, ray) => (ray, point!.Value.X, point.Value.Y)).ToList()]);
        }
        var result = mean.Result();
        var copiedResult = (List<(double X, double Y)>)averagerType.GetMethod("Take")!.Invoke(duplicate, null)!;
        Check(result.Count(point => point != null) == 3600, "PointMean keeps all 3600 exact 10 mm cluster-boundary rays");
        Check(copiedResult.Count == 3600, "legacy preview averager keeps all 3600 boundary rays");
        for (int ray = 0; ray < 3600; ray++)
        {
            double angle = ray / 10.0 * Math.PI / 180;
            var expected = new P(105 * Math.Cos(angle), 105 * Math.Sin(angle));
            Near(result[ray]!.Value.Distance(expected), 0, "PointMean boundary coordinate " + ray);
            Near(new P(copiedResult[ray].X, copiedResult[ray].Y).Distance(expected), 0, "preview boundary coordinate " + ray);
        }
        Check(((List<(double X, double Y)>)averagerType.GetMethod("Take")!.Invoke(duplicate, null)!).Count == 0,
            "preview averaging drains the block");
        var separated = new PointMean();
        var departed = new PointMean();
        for (int frame = 0; frame < 20; frame++)
        {
            separated.Add([new P(frame < 10 ? 100 : 110.00001, 0)]);
            departed.Add([new P(frame < 12 ? 100 : 200, 0)]);
        }
        Check(separated.Result().All(point => point == null), "rounding tolerance does not merge genuinely separated surfaces");
        Check(departed.Result().All(point => point == null), "old majority surface is rejected after a sustained exit");
        Console.WriteLine("PASS both 3600-ray cluster-boundary averagers");
    }

    internal sealed record ServiceCase(MeasuredAngleIron Part, LineScanAngleIronDto Dto, P[] RawPoints);

    static ServiceCase ProfileAndServiceChecks()
    {
        using var references = JsonDocument.Parse(File.ReadAllText(Path.Combine(Fixtures, "scene-reference.json")));
        Check(references.RootElement.GetArrayLength() == 7, "seven independently verified source profiles");
        ServiceCase? last = null;
        foreach (var expected in references.RootElement.EnumerateArray())
        {
            string file = expected.GetProperty("file").GetString()!;
            using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(Fixtures, file)));
            var root = document.RootElement;
            var input = root.GetProperty("selected_points").EnumerateArray().Select(Point).ToArray();
            var saved = root.GetProperty("calibration");
            var calibration = new Calibration(Point(saved.GetProperty("first")), Point(saved.GetProperty("second")));
            Check(input.Length == expected.GetProperty("selected").GetInt32(), file + " selected count");
            Check(root.GetProperty("points").GetArrayLength() == expected.GetProperty("all").GetInt32(), file + " whole count");
            VerifyProfile(input, calibration, expected, false, file);
            var mirrored = input.Select(point => new P(-point.X, point.Y)).ToArray();
            var mirrorCal = new Calibration(new(-calibration.Second.X, calibration.Second.Y), new(-calibration.First.X, calibration.First.Y));
            VerifyProfile(mirrored, mirrorCal, expected, true, file + " mirrored");
            last = VerifyService(input, calibration, expected, file);
            if (file == "measurement_639301361399916562.json")
                VerifyService(input, calibration, expected, file + " reverse encoder direction", reverse: true);
        }
        Console.WriteLine("PASS seven scene profiles, seven mirrors and two-frame active measurement/mapping");
        return last!;
    }

    static void VerifyProfile(P[] input, Calibration calibration, JsonElement expected, bool mirror, string name)
    {
        var before = input.ToArray();
        var profiles = Profile.Measure(input, calibration, autoCalibrate: true, autoModel: Profile.SceneContactModel);
        Check(input.SequenceEqual(before), name + " preserves source points");
        Check(profiles.Count == 1, name + " one profile");
        var profile = profiles[0];
        Near(profile.LeftMm, expected.GetProperty(mirror ? "RightMm" : "LeftMm").GetDouble(), name + " left");
        Near(profile.RightMm, expected.GetProperty(mirror ? "LeftMm" : "RightMm").GetDouble(), name + " right");
        Near(profile.WidthMm, expected.GetProperty("WidthMm").GetDouble(), name + " width");
        Near(profile.CorrectedHeightMm, expected.GetProperty("CorrectedHeightMm").GetDouble(), name + " peak height");
        Check(profile.AutoCalibrationModel == "scan_order_contacts_v3", name + " explicit scene model");
        Check(profile.LegPoints?.Length == 3, name + " displayed feature geometry");
        var local = profile.LegPoints!.Select(calibration.Transform).ToArray();
        var reference = expected.GetProperty("local_points");
        double baselineLength = calibration.First.Distance(calibration.Second);
        for (int i = 0; i < 3; i++)
        {
            var point = Point(reference[mirror ? 2 - i : i]);
            if (mirror) point = new P(baselineLength - point.X, point.Y);
            Near(local[i].Distance(point), 0, name + " displayed point " + i);
        }
        Near(local[0].Distance(local[1]), profile.LeftMm, name + " left label/geometry");
        Near(local[2].Distance(local[1]), profile.RightMm, name + " right label/geometry");
        Check(profile.FaceChordRmsMm.Length == 2 && profile.FaceChordRmsMm.All(value => value <= 2), name + " face RMS guard");
        Check(profile.FaceChordP95Mm.Length == 2 && profile.FaceChordP95Mm.All(value => value <= 3), name + " face P95 guard");
        Check(profile.ApexFitShiftMm is >= 0 and <= 10, name + " bounded fitted apex");
        Near(profile.ApexFitShiftMm!.Value, expected.GetProperty("ApexFitShiftMm").GetDouble(), name + " apex shift reference");
        for (int i = 0; i < 2; i++)
        {
            Near(profile.FaceChordRmsMm[i], expected.GetProperty("FaceChordRmsMm")[mirror ? 1 - i : i].GetDouble(), name + " face RMS reference " + i);
            Near(profile.FaceChordP95Mm[i], expected.GetProperty("FaceChordP95Mm")[mirror ? 1 - i : i].GetDouble(), name + " face P95 reference " + i);
        }
        var actions = expected.GetProperty("AutoEndpointActions").EnumerateArray().Select(value => value.GetString()).ToArray();
        if (mirror) Array.Reverse(actions);
        Check(profile.AutoEndpointActions.SequenceEqual(actions), name + " supported endpoint actions");
        Check(local[mirror ? 0 : 2].Y > 4, name + " shallow end remains above baseline");
        Near(local[mirror ? 2 : 0].Y, 0, name + " supported steep baseline contact");
    }

    static ServiceCase VerifyService(P[] input, Calibration calibration, JsonElement expected, string name, bool reverse = false)
    {
        var level = input.Select(calibration.Transform).ToArray();
        var vertices = new List<Vertex>();
        for (uint frame = 0; frame < 2; frame++)
            vertices.AddRange(level.Select((point, ray) => new Vertex(point.X, 37.5 + (reverse ? -1 : 1) * frame * 120, point.Y,
                frame, 1000 + frame * 1000, (ushort)frame, (ushort)ray, frame * .001)));
        var cloud = new Cloud(vertices, false, PositionMethod: "encoder_roll_travel");
        var irons = new LineScanMeasureService().MeasureLine(cloud, calibration, CancellationToken.None);
        Check(irons.Count == 1, name + " active service accepts exactly two valid encoder-positioned frames");
        var iron = irons[0];
        var legs = expected.GetProperty("local_points").EnumerateArray().Select(Point).ToArray();
        VerifyFeature(iron.HeadLeft, legs[0], 37.5, name + " head left");
        VerifyFeature(iron.HeadApex, legs[1], 37.5, name + " head apex");
        VerifyFeature(iron.HeadRight, legs[2], 37.5, name + " head right");
        double tailTravel = reverse ? -82.5 : 157.5;
        VerifyFeature(iron.TailLeft, legs[0], tailTravel, name + " tail left");
        VerifyFeature(iron.TailApex, legs[1], tailTravel, name + " tail apex");
        VerifyFeature(iron.TailRight, legs[2], tailTravel, name + " tail right");
        // Existing API definitions are deliberately preserved: feet chord distance and height above that chord.
        Near(iron.Width, legs[0].Distance(legs[2]), name + " established API width");
        double feetHeightAtPeak = legs[0].Y + (legs[2].Y - legs[0].Y) * (legs[1].X - legs[0].X) / (legs[2].X - legs[0].X);
        Near(iron.Height, legs[1].Y - feetHeightAtPeak, name + " established API height");
        Near(iron.LeftWidth, expected.GetProperty("LeftMm").GetDouble(), name + " left wing width");
        Near(iron.RightWidth, expected.GetProperty("RightMm").GetDouble(), name + " right wing width");
        Near(iron.Length, 120, name + " encoder apex travel");
        Near(iron.LeftLength, 120, name + " left encoder travel");
        Near(iron.RightLength, 120, name + " right encoder travel");
        var mapper = typeof(ScanAppService).GetMethod("ToAngleIron", BindingFlags.Static | BindingFlags.NonPublic)!;
        var dto = (LineScanAngleIronDto)mapper.Invoke(null, [iron])!;
        Near(dto.LeftWidth, iron.LeftWidth, name + " application maps left wing");
        Near(dto.RightWidth, iron.RightWidth, name + " application maps right wing");
        Near(dto.Width, iron.Width, name + " application maps established width");
        Near(dto.Height, iron.Height, name + " application maps established height");
        return new(iron, dto, input);
    }

    static void VerifyFeature(MeasuredPoint actual, P expected, double travel, string name)
    {
        Near(actual.X, expected.X, name + " X");
        Near(actual.Y, travel, name + " encoder Y");
        Near(actual.Z, expected.Y, name + " Z");
    }
}
