using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using ThreeDScan.Infrastructure.LineScan;
using ThreeDScan.Infrastructure.LineScan.Hank;

namespace ThreeDScan.LineScan.Regression;

internal static class ContextChecks
{
    internal static async Task RunAsync()
    {
        string directory = Path.Combine(Path.GetTempPath(), "ThreeDScanContextRegression_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            const string name = "measurement_639301351780719706.json";
            using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(Program.Fixtures, name)));
            using var references = JsonDocument.Parse(File.ReadAllText(Path.Combine(Program.Fixtures, "scene-reference.json")));
            var expected = references.RootElement.EnumerateArray().Single(value => value.GetProperty("file").GetString() == name);
            var root = fixture.RootElement;
            P Read(JsonElement value) => new(value[0].GetDouble(), value[1].GetDouble());
            var savedCalibration = root.GetProperty("calibration");
            var calibration = new Calibration(Read(savedCalibration.GetProperty("first")), Read(savedCalibration.GetProperty("second")));
            var fullPoints = root.GetProperty("points").EnumerateArray().Select(value => (P?)Read(value)).ToArray();
            var settings = new ReconstructionOptions
            {
                PositionSource = "encoder", AverageScans = 1, MinHeight = 3, MaxHeight = 500,
                WheelRadiusMm = 100 / (2 * Math.PI), EncoderCountsPerRev = 1000, EncoderChannel = 1,
                RoiXY = root.GetProperty("roi").EnumerateArray().Select(value => value.GetDouble()).ToArray()
            };
            string raw = Path.Combine(directory, "line_raw.ply");
            var recorder = new Recorder(raw, settings.RoiXY, new
            {
                calibration = new
                {
                    first = new[] { calibration.First.X, calibration.First.Y },
                    second = new[] { calibration.Second.X, calibration.Second.Y }
                },
                reconstruction_settings = settings
            });
            for (ushort frame = 0; frame < 2; frame++)
                recorder.Submit(new Scan(frame, (uint)(1000 + frame * 1000), 600, 0, fullPoints)
                {
                    Encoders = [new EncoderReading((uint)(100 + frame * 1200), 0)]
                });
            await recorder.Stop();
            Program.Check(recorder.Error == null, "two-frame real Recorder completed raw PLY and encoder/capture sidecars");

            var fallback = new Calibration(new(-100, 600), new(100, 600));
            Program.Check(LineScanMeasurementContext.ReadCapturedCalibration(directory) == calibration,
                "context finds frozen calibration without current DB settings");
            var context = LineScanMeasurementContext.Load(directory, fallback, CancellationToken.None);
            Program.Check(context.Calibration == calibration, "frozen calibration overrides changed fallback calibration");
            var frames = context.Frames.ToArray();
            Program.Check(frames.Length == 2, "private streamed measurement context yields two valid frames");
            Program.Check(frames.All(frame => frame.Any(point => point.Z < settings.MinHeight)),
                "private measurement context retains nearby table support below public 3 mm crop");
            Program.Near(frames[0][0].Y, 0, "private context first recorded encoder origin");
            Program.Near(frames[1][0].Y, 120, "private context preserves frozen encoder wheel scale");
            var irons = new LineScanMeasureService().MeasureLine(context.Frames, context.Calibration, CancellationToken.None);
            Program.Check(irons.Count == 1, "active service measures real frozen-context recording");
            Program.Near(irons[0].LeftWidth, expected.GetProperty("LeftMm").GetDouble(), "recorded context supported left wing", .01);
            Program.Near(irons[0].RightWidth, expected.GetProperty("RightMm").GetDouble(), "recorded context supported right wing", .01);
            Program.Near(irons[0].HeadLeft.Z, 0, "recorded context retains true steep table contact", .01);
            Program.Check(irons[0].HeadRight.Z > 4, "recorded context does not force shallow face to table");
            Program.Near(irons[0].Length, 120, "recorded context actual encoder travel", .001);
            using (var deferredCancel = new CancellationTokenSource())
            {
                var lazyContext = LineScanMeasurementContext.Load(directory, fallback, deferredCancel.Token);
                deferredCancel.Cancel();
                bool enumerationCanceled = false;
                try { _ = lazyContext.Frames.ToArray(); }
                catch (OperationCanceledException) { enumerationCanceled = true; }
                Program.Check(enumerationCanceled, "lazy context honors cancellation after creation and before streaming");
            }

            string publicOutput = Path.Combine(directory, "line_raw_3d.ply");
            StreamingReconstruction.BuildAndSave(raw, publicOutput, calibration, settings);
            var publicCloud = Cloud.Read(publicOutput);
            Program.Check(publicCloud.Points.All(point => point.Z >= settings.MinHeight), "public 3D export retains configured 3 mm crop");
            Program.Check(publicCloud.Points.Count < frames.Sum(frame => frame.Count), "private table support does not change public point count");
            using (var report = JsonDocument.Parse(File.ReadAllText(Path.ChangeExtension(publicOutput, ".json"))))
                Program.Near(report.RootElement.GetProperty("settings").GetProperty("min_height").GetDouble(), 3,
                    "public reconstruction metadata retains original crop");

            string capturePath = Path.ChangeExtension(raw, ".capture.json");
            var capture = JsonNode.Parse(File.ReadAllText(capturePath))!.AsObject();
            capture.Remove("reconstruction_settings");
            File.WriteAllText(capturePath, capture.ToJsonString());
            var legacy = LineScanMeasurementContext.Load(directory, fallback, CancellationToken.None);
            Program.Check(legacy.Frames.Count() == 2, "legacy capture uses completed reconstruction's frozen settings");
            capture["complete"] = false;
            File.WriteAllText(capturePath, capture.ToJsonString());
            bool rejected = false;
            try { LineScanMeasurementContext.Load(directory, fallback, CancellationToken.None); }
            catch (InvalidDataException) { rejected = true; }
            Program.Check(rejected, "incomplete recording is rejected before measurement");
            capture["complete"] = true;
            File.WriteAllText(capturePath, capture.ToJsonString());
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            rejected = false;
            try { LineScanMeasurementContext.Load(directory, fallback, canceled.Token); }
            catch (OperationCanceledException) { rejected = true; }
            Program.Check(rejected, "measurement context honors cancellation");
            File.Delete(raw);
            rejected = false;
            try { LineScanMeasurementContext.Load(directory, fallback, CancellationToken.None); }
            catch (InvalidDataException) { rejected = true; }
            Program.Check(rejected, "missing raw capture cannot silently fall back to height-cropped 3D");
        }
        finally
        {
            // This tool owns exactly one GUID-named child of the system temp directory.
            string resolved = Path.GetFullPath(directory);
            string tempParent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar);
            if (!string.Equals(Path.GetDirectoryName(resolved), tempParent, StringComparison.OrdinalIgnoreCase)
                || !Path.GetFileName(resolved).StartsWith("ThreeDScanContextRegression_", StringComparison.Ordinal))
                throw new InvalidOperationException("Refusing to delete a path outside the owned test temp directory.");
            Directory.Delete(resolved, recursive: true);
        }
        Console.WriteLine("PASS real raw recorder -> frozen streamed context -> active measurement; public crop unchanged");
    }
}
