using System.Buffers.Binary;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ThreeDScan.Domain.Models;
using ThreeDScan.Domain.Repositories;
using ThreeDScan.Infrastructure.Configuration;
using ThreeDScan.Infrastructure.LineScan;
using ThreeDScan.Infrastructure.LineScan.Hank;

namespace ThreeDScan.LineScan.Regression;

internal static class EncoderSimulationHostChecks
{
    internal static async Task RunAsync()
    {
        string directory = Path.Combine(Path.GetTempPath(), "ThreeDScanSimulationHostRegression_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            foreach (bool enabled in new[] { false, true })
                await VerifyHostAsync(Path.Combine(directory, enabled ? "json-enabled" : "json-disabled"), enabled);
        }
        finally { EncoderSimulationChecks.DeleteOwnedTemp(directory, "ThreeDScanSimulationHostRegression_"); }
        Console.WriteLine("PASS real Host start/capture/finalize with JSON switch and localhost missing-encoder sensor");
    }

    static async Task VerifyHostAsync(string directory, bool enabled)
    {
        await using var sensor = new LoopbackSensor();
        var registry = Options.Create(new LineScannerRegistryOptions
        {
            ["regression"] = new()
            {
                Ip = "127.0.0.1", Port = sensor.Port, PositionSource = "encoder", AverageScans = 1,
                WheelRadiusMm = 100 / (2 * Math.PI), EncoderCountsPerRev = 1000, EncoderChannel = 1,
                ConnectionTimeoutSeconds = 2, Xmin = -10000, Xmax = 10000, MinHeight = 3, MaxHeight = 5000
            }
        });
        var calibrationRecord = new LineScannerSettingsRecord
        {
            ScannerId = "regression", CalibrationFirstX = -100, CalibrationFirstY = 3000,
            CalibrationSecondX = 100, CalibrationSecondY = 3000
        };
        var repository = InterfaceDouble.Create<ILineScannerSettingsRepository>((method, _) =>
            method.Name == nameof(ILineScannerSettingsRepository.GetAsync)
                ? Task.FromResult<LineScannerSettingsRecord?>(calibrationRecord)
                : throw new NotSupportedException(method.Name));
        var services = new ServiceCollection();
        services.AddSingleton(repository);
        using var provider = services.BuildServiceProvider();
        var resolver = new LineScanOptionsResolver(registry, provider.GetRequiredService<IServiceScopeFactory>());
        var policy = new LineScanEncoderSimulationPolicy(Options.Create(new LineScanEncoderSimulationOptions
        {
            Enabled = enabled, ForwardSpeedMmPerSec = 100
        }));
        var host = new LineScanDeviceHost(resolver, policy);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        if (!enabled)
        {
            bool rejected = false;
            try { await host.StartLineAsync(directory, "regression", 100, "regression", deadline.Token); }
            catch (LineScanDeviceException error) when (error.Code == "SCAN_ENCODER_UNAVAILABLE") { rejected = true; }
            Program.Check(rejected, "actual JSON-disabled Host rejects missing physical encoder channel");
            Program.Check(!Directory.Exists(directory) || !Directory.EnumerateFiles(directory).Any(),
                "real-mode missing encoder rejection does not fabricate a recording");
            return;
        }

        await host.StartLineAsync(directory, "regression", 100, "regression", deadline.Token);
        string taskId = Path.GetFileName(directory);
        var captures = (System.Collections.IDictionary)typeof(LineScanDeviceHost)
            .GetField("_captures", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host)!;
        Program.Check(captures.Contains(taskId), "actual JSON-enabled Host starts capture without physical encoder channel");
        object capture = captures[taskId]!;
        var session = (EncoderScanSession)capture.GetType().GetField("Session")!.GetValue(capture)!;
        try
        {
            while (session.Progress.Samples < 4)
                await Task.Delay(5, deadline.Token);
            Program.Check(session.Progress.SpanMm > 0, "actual live Host receives forward simulated travel from real sensor frames");
            await host.FinalizeLineAsync(taskId, deadline.Token);
            Program.Check(captures.Count == 0, "actual simulated Host finalization releases capture");
            var cloud = Cloud.Read(session.OutputPath);
            Program.Check(cloud.Simulation && cloud.Points.Select(point => point.Y).Distinct().Count() >= 2,
                "actual Host writes marked synthetic 3D with positive forward range");
            Program.Check(cloud.Points.Min(point => point.Y) >= 0 && cloud.Points.Max(point => point.Y) > 0,
                "actual Host preserves forward-only reconstruction convention");
            using var metadata = JsonDocument.Parse(File.ReadAllText(Path.ChangeExtension(session.RawPath, ".capture.json")));
            Program.Check(metadata.RootElement.GetProperty("encoder_simulation").GetProperty("enabled").GetBoolean()
                && metadata.RootElement.GetProperty("synthetic").GetBoolean(), "actual Host persists JSON-enabled simulation provenance");
        }
        finally
        {
            if (captures.Contains(taskId)) await host.AbortLineAsync(taskId);
        }
    }

    sealed class LoopbackSensor : IAsyncDisposable
    {
        readonly TcpListener listener;
        readonly CancellationTokenSource stopping = new();
        readonly Task sender;
        TcpClient? client;
        internal int Port { get; }

        internal LoopbackSensor()
        {
            listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            Port = ((IPEndPoint)listener.LocalEndpoint).Port;
            sender = SendAsync();
        }

        async Task SendAsync()
        {
            try
            {
                client = await listener.AcceptTcpClientAsync(stopping.Token);
                using var stream = client.GetStream();
                // Original independently referenced packet: real DIST1 XY, no encoder channel.
                using var fixtures = JsonDocument.Parse(File.ReadAllText(Path.Combine(Program.Fixtures, "protocol-python-reference.json")));
                byte[] payload = Convert.FromBase64String(fixtures.RootElement[0].GetProperty("payload_base64").GetString()!);
                for (ushort frame = 0; !stopping.IsCancellationRequested; frame++)
                {
                    byte[] next = payload.ToArray();
                    BinaryPrimitives.WriteUInt16BigEndian(next.AsSpan(28, 2), frame);
                    BinaryPrimitives.WriteUInt32BigEndian(next.AsSpan(30, 4), 1000u + (uint)frame * 10000);
                    await stream.WriteAsync(Protocol.Frame(next), stopping.Token);
                    await Task.Delay(10, stopping.Token);
                }
            }
            catch (Exception error) when (error is OperationCanceledException or IOException or SocketException or ObjectDisposedException)
            {
                // The tested lease closes its own socket during successful finalization/rejection.
            }
        }

        public async ValueTask DisposeAsync()
        {
            stopping.Cancel();
            client?.Dispose();
            listener.Stop();
            await sender;
            stopping.Dispose();
        }
    }
}
