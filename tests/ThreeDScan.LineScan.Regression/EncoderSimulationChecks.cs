using System.IO;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using ThreeDScan.Infrastructure;
using ThreeDScan.Infrastructure.Configuration;
using ThreeDScan.Infrastructure.LineScan;
using ThreeDScan.Infrastructure.LineScan.Hank;

namespace ThreeDScan.LineScan.Regression;

internal static class EncoderSimulationChecks
{
    internal static async Task RunAsync()
    {
        ConfigurationChecks();
        string directory = Path.Combine(Path.GetTempPath(), "ThreeDScanSimulationRegression_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var sessions = new List<EncoderScanSession>();
        try
        {
            using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(Program.Fixtures, "measurement_639301351780719706.json")));
            var root = fixture.RootElement;
            P Point(JsonElement value) => new(value[0].GetDouble(), value[1].GetDouble());
            var saved = root.GetProperty("calibration");
            var calibration = new Calibration(Point(saved.GetProperty("first")), Point(saved.GetProperty("second")));
            var points = root.GetProperty("points").EnumerateArray().Select(value => (P?)Point(value)).ToArray();
            var roi = root.GetProperty("roi").EnumerateArray().Select(value => value.GetDouble()).ToArray();
            foreach ((int direction, int average, int count) in new[] { (1, 1, 6), (-1, 20, 40) })
            {
                string sessionDirectory = Path.Combine(directory, "forward-" + direction);
                Directory.CreateDirectory(sessionDirectory);
                var settings = new ReconstructionOptions
                {
                    PositionSource = "encoder", AverageScans = average, Direction = direction, EncoderChannel = 2,
                    WheelRadiusMm = 100 / (2 * Math.PI), EncoderCountsPerRev = 1000, RoiXY = roi,
                    MinHeight = 3, MaxHeight = 500
                };
                var options = new LineScanEncoderSimulationOptions { Enabled = true, ForwardSpeedMmPerSec = 100 };
                var policy = new LineScanEncoderSimulationPolicy(Options.Create(options));
                var session = policy.StartSession(Path.Combine(sessionDirectory, "line_raw.ply"), calibration, settings);
                sessions.Add(session);
                // A later caller/configuration edit must not change an active recording's frozen scale/speed.
                options.ForwardSpeedMmPerSec = 900;
                options.Enabled = false;
                uint start = direction == 1 ? 1000u : uint.MaxValue - 20000;
                for (ushort frame = 0; frame < count; frame++)
                {
                    var input = new Scan(frame, unchecked(start + (uint)frame * 10000), 100, 0, points)
                    {
                        // Selected channel 2 is absent; unrelated real channel 1 must remain unchanged.
                        Encoders = [new EncoderReading(17, 9)]
                    };
                    session.Submit(input);
                    double beforeDuplicate = session.Progress.PositionMm;
                    session.Submit(input);
                    Program.Near(session.Progress.PositionMm, beforeDuplicate, "duplicate telegram never advances simulated travel");
                    Program.Check(input.Encoders.Count == 1 && input.Encoders[0] == new EncoderReading(17, 9),
                        "simulation never mutates the original scan's real encoder channels");
                }
                Program.Check(session.Recorder.Error == null, "valid and duplicate sensor frames do not corrupt capture: " + session.Recorder.Error);
                double expectedSpan = count - 1; // 100 mm/s * 0.010 s between real device timestamps.
                Program.Near(session.Progress.SpanMm, expectedSpan, "simulated forward span uses only device time", .101);
                Program.Check(session.Progress.Motion == "前進", "configured direction still produces forward positive Y");
                await session.StopAndBuildAsync();
                Program.Check(session.Recorder.Error == null, "simulated source recording completes with valid sensor frames");
                VerifyMarkersAndReplay(session, calibration, points, settings, average, count, expectedSpan);
                if (average == 20)
                    await VerifyAveragingContinuityAsync(session, calibration, points, settings, start, sessions);
            }
            await VerifyDisabledAndInvalidInputsAsync(directory, calibration, points, sessions);
        }
        finally
        {
            foreach (var session in sessions) await session.Recorder.Stop();
            DeleteOwnedTemp(directory, "ThreeDScanSimulationRegression_");
        }
        Console.WriteLine("PASS JSON-only simulation, forward signs, duplicate/time wrap, frozen replay and synthetic provenance");
    }

    static void ConfigurationChecks()
    {
        foreach (string environment in new[] { Environments.Development, Environments.Production })
        foreach (bool enabled in new[] { false, true })
        {
            string json = JsonSerializer.Serialize(new
            {
                ConnectionStrings = new { ThreeDScan = "Server=(local);Database=RegressionNeverOpened;Integrated Security=true;TrustServerCertificate=true" },
                LineScan = new { EncoderSimulation = new { Enabled = enabled, ForwardSpeedMmPerSec = 73.5 } }
            });
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
            var configuration = new ConfigurationBuilder().AddJsonStream(stream).Build();
            var services = new ServiceCollection();
            services.AddSingleton(InterfaceDouble.Create<IHostEnvironment>((method, _) => method.Name == "get_EnvironmentName"
                ? environment : throw new NotSupportedException(method.Name)));
            services.AddThreeDScanInfrastructure(configuration);
            using var provider = services.BuildServiceProvider();
            var policy = provider.GetRequiredService<LineScanEncoderSimulationPolicy>();
            Program.Check(policy.Enabled == enabled, "JSON alone controls simulation even in " + environment);
            Program.Check(policy.ForwardSpeedMmPerSec == (enabled ? 73.5 : null), "DI binds configured simulation speed");
        }
        Program.Check(!new LineScanEncoderSimulationPolicy(Options.Create(new LineScanEncoderSimulationOptions())).Enabled,
            "simulation is false by default");
        foreach (double speed in new[] { 0, -1, double.NaN, double.PositiveInfinity })
        {
            bool rejected = false;
            try { _ = new LineScanEncoderSimulationPolicy(Options.Create(new LineScanEncoderSimulationOptions { Enabled = true, ForwardSpeedMmPerSec = speed })); }
            catch (ArgumentException) { rejected = true; }
            Program.Check(rejected, "enabled simulation rejects invalid speed " + speed);
        }
    }

    static void VerifyMarkersAndReplay(EncoderScanSession session, Calibration calibration, P?[] originalPoints,
        ReconstructionOptions settings, int average, int uniqueFrames, double expectedSpan)
    {
        string directory = Path.GetDirectoryName(session.RawPath)!;
        var raw = Cloud.Read(session.RawPath);
        Program.Check(raw.Raw && raw.Simulation, "raw PLY carries explicit synthetic encoder source");
        Program.Check(File.ReadLines(session.RawPath).TakeWhile(line => line != "end_header").Contains("comment encoder_source synthetic"),
            "raw PLY header cannot be mistaken for physical encoder measurement");
        foreach (var point in raw.Points)
        {
            var original = originalPoints[point.Ray]!.Value;
            Program.Near(point.X, (float)original.X, "simulation preserves recorded scanner X", 0);
            Program.Near(point.Y, (float)original.Y, "simulation preserves recorded scanner Y", 0);
        }
        var lines = File.ReadAllLines(Path.ChangeExtension(session.RawPath, ".encoder.jsonl"));
        using (var header = JsonDocument.Parse(lines[0]))
            Program.Check(header.RootElement.GetProperty("simulation").GetBoolean()
                && header.RootElement.GetProperty("source").GetString() == "synthetic", "encoder sidecar header is explicitly synthetic");
        var positions = new Dictionary<(ushort Counter, uint Timestamp), uint>();
        foreach (string line in lines.Skip(1))
        {
            using var row = JsonDocument.Parse(line);
            var value = row.RootElement;
            if (value.GetProperty("type").GetString() != "frame") continue;
            var key = (value.GetProperty("scan_counter").GetUInt16(), value.GetProperty("timestamp_us").GetUInt32());
            Program.Check(value.GetProperty("positions")[0].GetUInt32() == 17, "simulation preserves unrelated real encoder channel");
            uint position = value.GetProperty("positions")[1].GetUInt32();
            if (positions.TryGetValue(key, out uint prior)) Program.Check(prior == position, "duplicate sidecar frame preserves simulated position");
            positions[key] = position;
        }
        Program.Check(positions.Count == uniqueFrames, "simulation records exactly the distinct incoming frame identities");
        using (var capture = JsonDocument.Parse(File.ReadAllText(Path.ChangeExtension(session.RawPath, ".capture.json"))))
        {
            var value = capture.RootElement;
            Program.Check(value.GetProperty("synthetic").GetBoolean() && value.GetProperty("encoder_source").GetString() == "synthetic",
                "capture provenance is synthetic");
            var simulation = value.GetProperty("encoder_simulation");
            Program.Check(simulation.GetProperty("enabled").GetBoolean()
                && simulation.GetProperty("time_source").GetString() == "device_timestamp_us", "capture identifies device-clock simulation");
            Program.Near(simulation.GetProperty("forward_speed_mm_per_sec").GetDouble(), 100, "recording freezes original simulation speed");
        }
        var output = Cloud.Read(session.OutputPath);
        Program.Check(output.Simulation, "completed 3D PLY remains synthetic");
        using (var report = JsonDocument.Parse(File.ReadAllText(Path.ChangeExtension(session.OutputPath, ".json"))))
            Program.Check(report.RootElement.GetProperty("simulation").GetBoolean()
                && report.RootElement.GetProperty("position_reference").GetProperty("source").GetString() == "synthetic",
                "completed report remains synthetic");
        var publicY = output.Points.GroupBy(point => point.Frame).OrderBy(frame => frame.Key).Select(frame => frame.First().Y).ToArray();
        Program.Check(publicY.Length == uniqueFrames / average, "simulation reuses real block-averaging pipeline");
        Program.Check(publicY[^1] > publicY[0], "public simulated output moves forward");
        double expectedFirst = (average - 1) * .5;
        Program.Near(publicY[0], expectedFirst, "mean simulated position for first block", .101);
        Program.Near(publicY[^1], expectedSpan - expectedFirst, "mean simulated position for last block", .101);
        var replay = LineScanMeasurementContext.Load(directory, new Calibration(new(-100, 600), new(100, 600)), CancellationToken.None);
        Program.Check(replay.Calibration == calibration, "simulated replay uses frozen real scanner calibration");
        var frames = replay.Frames.ToArray();
        Program.Check(frames.Length == publicY.Length, "frozen measurement replay preserves averaged frame count");
        for (int i = 0; i < frames.Length; i++) Program.Near(frames[i][0].Y, publicY[i], "frozen replay uses saved synthetic encoder counts", 1e-5);
        if (frames.Length >= 2)
        {
            var measured = new LineScanMeasureService().MeasureLine(replay.Frames, replay.Calibration, CancellationToken.None);
            Program.Check(measured.Count == 1, "real profile geometry remains measurable under simulated travel");
            Program.Near(measured[0].Length, publicY[^1] - publicY[0], "active measurement uses frozen simulated travel", .001);
        }
    }

    static async Task VerifyDisabledAndInvalidInputsAsync(string directory, Calibration calibration, P?[] points,
        ICollection<EncoderScanSession> sessions)
    {
        var settings = new ReconstructionOptions { PositionSource = "encoder", AverageScans = 1 };
        string realDirectory = Path.Combine(directory, "json-disabled");
        Directory.CreateDirectory(realDirectory);
        var disabled = new LineScanEncoderSimulationPolicy(Options.Create(new LineScanEncoderSimulationOptions { Enabled = false }));
        var real = disabled.StartSession(Path.Combine(realDirectory, "line_raw.ply"), calibration, settings);
        sessions.Add(real);
        for (ushort i = 0; i < 2; i++) real.Submit(new Scan(i, (uint)(1000 + i * 1000), 600, 0, points) { Encoders = [new(0, 0)] });
        bool rejected = false;
        try { await real.StopAndBuildAsync(); }
        catch (ArgumentException) { rejected = true; }
        Program.Check(rejected && !File.Exists(real.OutputPath), "JSON-disabled policy preserves real no-motion guard");
        Program.Check(!Cloud.Read(real.RawPath).Simulation, "JSON-disabled recording remains marked real");

        string movingDirectory = Path.Combine(directory, "real-duplicate-baseline");
        Directory.CreateDirectory(movingDirectory);
        var moving = disabled.StartSession(Path.Combine(movingDirectory, "line_raw.ply"), calibration, settings);
        sessions.Add(moving);
        var baseline = new Scan(0, 1000, 600, 0, points) { Encoders = [new(100, 0)] };
        moving.Submit(baseline);
        moving.Submit(baseline);
        moving.Submit(new Scan(1, 2000, 600, 0, points) { Encoders = [new(200, 0)] });
        await moving.StopAndBuildAsync();
        Program.Check(moving.Recorder.Error == null && !Cloud.Read(moving.OutputPath).Simulation,
            "identical real baseline duplicate remains valid when later physical encoder moves");
        Program.Near(moving.Progress.SpanMm, 100 * 2 * Math.PI * settings.WheelRadiusMm / settings.EncoderCountsPerRev,
            "real baseline duplicate does not add physical travel");

        string conflictingDirectory = Path.Combine(directory, "real-conflicting-duplicate");
        Directory.CreateDirectory(conflictingDirectory);
        var conflicting = disabled.StartSession(Path.Combine(conflictingDirectory, "line_raw.ply"), calibration, settings);
        sessions.Add(conflicting);
        conflicting.Submit(baseline);
        conflicting.Submit(baseline with { Encoders = [new EncoderReading(101, 0)] });
        rejected = false;
        try { await conflicting.StopAndBuildAsync(); }
        catch (InvalidDataException) { rejected = true; }
        Program.Check(rejected && conflicting.Recorder.Error?.Contains("衝突") == true && !File.Exists(conflicting.OutputPath),
            "conflicting real encoder positions on a duplicate frame remain invalid");

        var enabled = new LineScanEncoderSimulationPolicy(Options.Create(new LineScanEncoderSimulationOptions { Enabled = true }));
        foreach (string failure in new[] { "no-frames", "device-fault", "clock-reset" })
        {
            string path = Path.Combine(directory, failure);
            Directory.CreateDirectory(path);
            var session = enabled.StartSession(Path.Combine(path, "line_raw.ply"), calibration, settings);
            sessions.Add(session);
            if (failure == "device-fault") session.Submit(new Scan(0, 1000, 600, 1, points));
            if (failure == "clock-reset")
            {
                session.Submit(new Scan(0, 1000, 600, 0, points));
                session.Submit(new Scan(1, 1000, 600, 0, points));
            }
            if (failure == "no-frames")
            {
                await Task.Delay(20);
                Program.Check(session.Progress.Samples == 0 && session.Progress.SpanMm == 0, "wall time alone never invents simulated sensor frames");
            }
            rejected = false;
            try { await session.StopAndBuildAsync(); }
            catch (Exception error) when (error is ArgumentException or InvalidDataException) { rejected = true; }
            Program.Check(rejected && !File.Exists(session.OutputPath), "simulation retains capture validity guard: " + failure);
        }
    }

    static async Task VerifyAveragingContinuityAsync(EncoderScanSession duplicated, Calibration calibration,
        P?[] points, ReconstructionOptions settings, uint start, ICollection<EncoderScanSession> sessions)
    {
        var duplicatedCloud = Cloud.Read(duplicated.OutputPath);
        var legacyDuplicated = ReconstructionIO.Build(Cloud.Read(duplicated.RawPath), calibration, settings);
        CompareClouds(duplicatedCloud, legacyDuplicated, "duplicate avg20 streaming/legacy parity");
        Program.Check(duplicatedCloud.Points.Select(point => point.Frame).Distinct().SequenceEqual(new uint[] { 0, 40 }),
            "duplicate averaging keeps original raw source frame IDs");

        foreach (string kind in new[] { "canonical", "counter-gap", "empty-frame" })
        {
            string directory = Path.Combine(Path.GetDirectoryName(duplicated.RawPath)!, kind);
            Directory.CreateDirectory(directory);
            var policy = new LineScanEncoderSimulationPolicy(Options.Create(new LineScanEncoderSimulationOptions { Enabled = true }));
            var session = policy.StartSession(Path.Combine(directory, "line_raw.ply"), calibration, settings);
            sessions.Add(session);
            for (ushort frame = 0; frame < 40; frame++)
            {
                ushort counter = (ushort)(frame + (kind == "counter-gap" && frame >= 10 ? 1 : 0));
                var geometry = kind == "empty-frame" && frame == 10 ? new P?[points.Length] : points;
                session.Submit(new Scan(counter, unchecked(start + (uint)frame * 10000), 100, 0, geometry)
                {
                    Encoders = [new EncoderReading(17, 9)]
                });
            }
            await session.StopAndBuildAsync();
            var streamed = Cloud.Read(session.OutputPath);
            var legacy = ReconstructionIO.Build(Cloud.Read(session.RawPath), calibration, settings);
            CompareClouds(streamed, legacy, kind + " streaming/legacy parity");
            var frames = streamed.Points.GroupBy(point => point.Frame).OrderBy(frame => frame.Key).ToArray();
            if (kind == "canonical")
            {
                CompareClouds(duplicatedCloud, streamed, "avg20 duplicate/non-duplicate geometry parity");
                Program.Check(frames.Select(frame => frame.Key).SequenceEqual(new uint[] { 0, 20 }),
                    "canonical averaging uses its own unchanged source IDs");
            }
            else
            {
                double[] expectedY = kind == "counter-gap" ? [4.5, 19.5, 34.5] : [4.5, 20.5, 35];
                Program.Check(frames.Length == 3, "averaging must split at " + kind);
                for (int i = 0; i < frames.Length; i++)
                    Program.Near(frames[i].First().Y, expectedY[i], "averaging retains separate position block: " + kind, .101);
            }
        }
        Console.WriteLine("PASS avg20 duplicate/canonical/legacy parity and real-gap/empty-frame block separation");
    }

    static void CompareClouds(Cloud actual, Cloud expected, string name)
    {
        var actualFrames = actual.Points.GroupBy(point => point.Frame).OrderBy(frame => frame.Key).ToArray();
        var expectedFrames = expected.Points.GroupBy(point => point.Frame).OrderBy(frame => frame.Key).ToArray();
        Program.Check(actualFrames.Length == expectedFrames.Length, name + " frame count");
        for (int frame = 0; frame < actualFrames.Length; frame++)
        {
            var a = actualFrames[frame].OrderBy(point => point.Ray).ToArray();
            var b = expectedFrames[frame].OrderBy(point => point.Ray).ToArray();
            Program.Check(a.Length == b.Length, name + " ray count");
            for (int ray = 0; ray < a.Length; ray++)
            {
                Program.Check(a[ray].Ray == b[ray].Ray && a[ray].Counter == b[ray].Counter
                    && a[ray].Timestamp == b[ray].Timestamp, name + " source ray/counter/time");
                Program.Near(a[ray].X, b[ray].X, name + " X", 1e-4);
                Program.Near(a[ray].Y, b[ray].Y, name + " Y", 1e-4);
                Program.Near(a[ray].Z, b[ray].Z, name + " Z", 1e-4);
            }
        }
    }

    internal static void DeleteOwnedTemp(string directory, string prefix)
    {
        string resolved = Path.GetFullPath(directory);
        string parent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar);
        if (!string.Equals(Path.GetDirectoryName(resolved), parent, StringComparison.OrdinalIgnoreCase)
            || !Path.GetFileName(resolved).StartsWith(prefix, StringComparison.Ordinal))
            throw new InvalidOperationException("Refusing to delete outside the owned simulation temp directory.");
        Directory.Delete(resolved, recursive: true);
    }
}
