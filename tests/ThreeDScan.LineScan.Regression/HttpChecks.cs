using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ThreeDScan.Api.Controllers;
using ThreeDScan.Application.Common.Models;
using ThreeDScan.Application.Scan;
using ThreeDScan.Application.Scan.Models;
using ThreeDScan.Domain.Models;

namespace ThreeDScan.LineScan.Regression;

internal static class HttpChecks
{
    internal static async Task RunAsync(Program.ServiceCase measured)
    {
        var proxy = DispatchProxy.Create<IScanAppService, ScanApplicationBridge>();
        var bridge = (ScanApplicationBridge)(object)proxy;
        bridge.Measured = measured;
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Testing",
            ContentRootPath = Path.GetTempPath(),
            ApplicationName = typeof(ScanController).Assembly.FullName
        });
        builder.Logging.ClearProviders();
        builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
        builder.Services.AddControllers().AddApplicationPart(typeof(ScanController).Assembly);
        builder.Services.AddSingleton(proxy);
        await using var app = builder.Build();
        Program.Check(app.Environment.IsEnvironment("Testing"), "HTTP test host is isolated Testing environment");
        // Intentionally host real controllers only. Production Program, DB bootstraps, auth configuration,
        // hosted workers, physical devices and callbacks are never started or changed by this test.
        app.MapControllers();
        await app.StartAsync();
        try
        {
            var server = app.Services.GetRequiredService<IServer>();
            string address = server.Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            Program.Check(new Uri(address).IsLoopback, "HTTP test listens on loopback only");
            using var client = new HttpClient { BaseAddress = new Uri(address) };

            using var profileResponse = await client.PostAsJsonAsync("/api/scan/profile", new { scannerId = "regression" });
            Program.Check(profileResponse.StatusCode == HttpStatusCode.OK, "real profile controller returns 200");
            using var profile = JsonDocument.Parse(await profileResponse.Content.ReadAsStringAsync());
            var points = profile.RootElement.GetProperty("points");
            Program.Check(points.GetArrayLength() == measured.RawPoints.Length, "HTTP profile retains full supplied point count");
            for (int i = 0; i < points.GetArrayLength(); i++)
            {
                Program.Near(points[i].GetProperty("x").GetDouble(), measured.RawPoints[i].X, "HTTP exact profile X " + i, 0);
                Program.Near(points[i].GetProperty("y").GetDouble(), measured.RawPoints[i].Y, "HTTP exact profile Y " + i, 0);
            }
            Program.Check(profile.RootElement.GetProperty("coordinateFrame").GetString() == "scanner_xy", "HTTP profile scanner coordinate contract");
            Program.Check(profile.RootElement.GetProperty("averageScans").GetInt32() == 20, "HTTP profile averaging metadata");

            using var liveResponse = await client.GetAsync("/api/scan/line/live");
            Program.Check(liveResponse.IsSuccessStatusCode, "real live controller returns 200");
            using var live = JsonDocument.Parse(await liveResponse.Content.ReadAsStringAsync());
            Program.Check(live.RootElement.GetProperty("x").GetArrayLength() == measured.RawPoints.Length, "HTTP live count matches profile");
            Program.Near(live.RootElement.GetProperty("y")[0].GetDouble(), measured.RawPoints[0].Y, "HTTP live scanner Y stays unscaled", 0);

            using var complete = await client.PostAsJsonAsync("/api/scan/line/complete",
                new { taskId = "regression-task", callbackUrl = "http://127.0.0.1/unused-test-callback" });
            Program.Check(complete.StatusCode == HttpStatusCode.Accepted, "real complete controller returns 202");
            Program.Check(bridge.CompleteCalls == 1, "complete is handled by test bridge without a physical capture");
            foreach (string route in new[] { "/api/scan/line/latest", "/api/tasks/regression-task/result" })
            {
                using var response = await client.GetAsync(route);
                Program.Check(response.StatusCode == HttpStatusCode.OK, route + " returns 200");
                using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                Program.Check(body.RootElement.GetProperty("status").GetString() == "Completed", route + " completed contract");
                var iron = body.RootElement.GetProperty("result").GetProperty("angleIrons")[0];
                Program.Near(iron.GetProperty("leftWidth").GetDouble(), measured.Dto.LeftWidth, route + " current supported left width");
                Program.Near(iron.GetProperty("rightWidth").GetDouble(), measured.Dto.RightWidth, route + " current supported right width");
                Program.Near(iron.GetProperty("width").GetDouble(), measured.Dto.Width, route + " established width contract");
                Program.Near(iron.GetProperty("height").GetDouble(), measured.Dto.Height, route + " established height contract");
                Program.Near(iron.GetProperty("headFeatures").GetProperty("rightCorner").GetProperty("z").GetDouble(),
                    measured.Part.HeadRight.Z, route + " right feature stays on observed face above baseline");
                Program.Check(!iron.TryGetProperty("autoCalibrationModel", out _), route + " public DAS schema does not acquire internal diagnostic fields");
            }

            using var invalid = await client.PostAsJsonAsync("/api/scan/line/complete", new { taskId = "regression-task" });
            Program.Check(invalid.StatusCode == HttpStatusCode.BadRequest && bridge.CompleteCalls == 1,
                "real API validation rejects missing callback before application call");
            using var malformed = await client.PostAsync("/api/scan/profile", new StringContent("{\"scannerId\":17}", Encoding.UTF8, "application/json"));
            Program.Check(malformed.StatusCode == HttpStatusCode.BadRequest, "real JSON model binding rejects non-string scanner id");
            using var failure = await client.PostAsJsonAsync("/api/scan/profile", new { scannerId = "test-failure" });
            Program.Check(failure.StatusCode == HttpStatusCode.BadRequest, "real profile controller maps application failure to 400");
            using var error = JsonDocument.Parse(await failure.Content.ReadAsStringAsync());
            Program.Check(error.RootElement.GetProperty("error").GetProperty("code").GetString() == "TEST_CAPTURE_FAILURE", "existing API error contract retained");
        }
        finally
        {
            await app.StopAsync();
        }
        Console.WriteLine("PASS isolated loopback HTTP: real capture/live/complete/latest/task controllers and existing JSON contracts");
    }
}

// Only application I/O is replaced. Profile geometry and 3D measurement were computed by the real
// Infrastructure service, then mapped by the real Application mapper before reaching these controllers.
public class ScanApplicationBridge : DispatchProxy
{
    internal Program.ServiceCase Measured { get; set; } = null!;
    internal int CompleteCalls { get; private set; }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        switch (targetMethod!.Name)
        {
            case nameof(IScanAppService.CaptureLineProfileAsync):
                if (((LineScanProfileRequest)args![0]!).ScannerId == "test-failure")
                    return Task.FromResult(DasOperationResult<LineScanProfileData>.Fail("TEST_CAPTURE_FAILURE", "Intentional test failure"));
                return Task.FromResult(DasOperationResult<LineScanProfileData>.Ok(new()
                {
                    ScannerId = "regression", X = Measured.RawPoints.Select(point => point.X).ToArray(),
                    Y = Measured.RawPoints.Select(point => point.Y).ToArray(), AverageScans = 20,
                    CapturedAt = "2026-11-13T00:00:00Z", TraceId = "regression"
                }));
            case nameof(IScanAppService.GetLiveFrameAsync):
                return Task.FromResult(new LineScanLiveFrame
                {
                    X = Measured.RawPoints.Select(point => point.X).ToArray(),
                    Y = Measured.RawPoints.Select(point => point.Y).ToArray()
                });
            case nameof(IScanAppService.CompleteLineScanAsync):
                CompleteCalls++;
                return Task.FromResult(DasOperationResult<AsyncAcceptedData>.Ok(new() { TaskId = "regression-task" }));
            case nameof(IScanAppService.GetLatestLineScanResult):
                return Result();
            case nameof(IScanAppService.GetTaskResultAsync):
                return Task.FromResult(DasOperationResult<TaskResultData>.Ok(Result()));
            default:
                throw new NotSupportedException("Regression bridge must not start other application operations: " + targetMethod.Name);
        }
    }

    TaskResultData Result() => new()
    {
        TaskId = "regression-task", Status = TaskResultData.StatusCompleted, TraceId = "regression",
        Result = new LineScanCompleteResult { DetectedCount = 1, AngleIrons = [Measured.Dto] }
    };
}
