using System.IO;
using System.Text.Json;
using ThreeDScan.Infrastructure.LineScan;
using ThreeDScan.Infrastructure.LineScan.Hank;

namespace ThreeDScan.LineScan.Regression;

internal static class MeasurementDiagnosticsChecks
{
    internal static void Run()
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(Program.Fixtures,
            "measurement_639301351780719706.json")));
        var root = fixture.RootElement;
        static P Point(JsonElement value) => new(value[0].GetDouble(), value[1].GetDouble());
        var saved = root.GetProperty("calibration");
        var calibration = new Calibration(Point(saved.GetProperty("first")), Point(saved.GetProperty("second")));
        var local = root.GetProperty("selected_points").EnumerateArray()
            .Select(value => calibration.Transform(Point(value))).ToArray();
        IReadOnlyList<Vertex> Frame(uint index, double travel) => local.Select((p, ray) =>
            new Vertex(p.X, travel, p.Y, index, 1000 + index * 1000, (ushort)index, (ushort)ray, index * .001)).ToArray();
        string Failure(IEnumerable<IReadOnlyList<Vertex>> frames)
        {
            try { new LineScanMeasureService().MeasureLine(frames, calibration, CancellationToken.None); }
            catch (InvalidDataException error) { return error.Message; }
            throw new InvalidOperationException("Invalid recording unexpectedly produced a measurement.");
        }

        var empty = Failure(Array.Empty<IReadOnlyList<Vertex>>());
        Program.Check(empty.Contains("未找到完整角鋼截面") && !empty.Contains("Encoder"),
            "no detected geometry reports the profile problem without claiming missing encoder travel");
        var single = Failure([Frame(0, 0)]);
        Program.Check(single.Contains("單一截面") && single.Contains("X=") && single.Contains("1 個截面")
            && !single.Contains("Encoder"), "single-frame track failure includes its location and sample count");
        var stationary = Failure([Frame(0, 0), Frame(1, 0)]);
        Program.Check(stationary.Contains("沒有有效 Encoder 行程") && stationary.Contains("2 個截面"),
            "matched profiles with unchanged position report encoder travel separately");
        Console.WriteLine("PASS measurement diagnostics distinguish missing geometry, transient tracks and zero encoder travel");
    }
}
