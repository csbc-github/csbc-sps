using System.Text.Json;
using Microsoft.Extensions.Options;
using ThreeDScan.Domain.Abstractions;
using ThreeDScan.Domain.Models;
using ThreeDScan.Infrastructure.Configuration;
using ThreeDScan.Infrastructure.LineScan.Hank;

namespace ThreeDScan.Infrastructure.LineScan;

/// <summary>
/// 原生 SICK／Encoder 線掃描（Hank Core 埠）。以 <see cref="LineScanDeviceHost"/> 取代 Python 工作程序。
/// </summary>
public sealed class SickCSharpLineScanner : ISickLineScanner
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly LineScanDeviceHost _host;
    private readonly LineScanOptionsResolver _resolver;
    private readonly LineScanMeasureService _measure;
    private readonly LineScannerRegistryOptions _registry;

    /// <summary>建立 C# 線掃描執行器。</summary>
    public SickCSharpLineScanner(
        LineScanDeviceHost host,
        LineScanOptionsResolver resolver,
        LineScanMeasureService measure,
        IOptions<LineScannerRegistryOptions> registry)
    {
        _host = host;
        _resolver = resolver;
        _measure = measure;
        _registry = registry.Value;
    }

    /// <inheritdoc />
    public async Task<RollerSampleResult> SampleRollerAsync(
        string ip,
        int port,
        double toleranceMm,
        double rangeDeviationMm,
        int frames,
        CancellationToken ct = default)
    {
        _ = toleranceMm;
        _ = rangeDeviationMm;
        _ = frames;
        var scannerId = ResolveScannerId(ip, port);
        if (scannerId is null)
        {
            return Failed("CAL_CFG_001", "無法由 IP／埠對應線掃描 scannerId。");
        }

        try
        {
            var zDistance = await _host.CalibrateRollerAsync(scannerId, ct).ConfigureAwait(false);
            return new RollerSampleResult
            {
                Clear = true,
                ZDistance = zDistance,
            };
        }
        catch (LineScanDeviceException ex)
        {
            return new RollerSampleResult
            {
                Clear = false,
                Code = ex.Code,
                Message = ex.Message,
            };
        }
    }

    /// <inheritdoc />
    public async Task StartRecordingAsync(string sessionDirectory, string ip, int port, CancellationToken ct = default)
    {
        var context = ReadSessionContext(sessionDirectory, ip, port);
        Directory.CreateDirectory(sessionDirectory);
        var meta = new LineScanSessionMeta
        {
            ScannerId = context.ScannerId,
            TaskId = context.TaskId,
            TraceId = context.TraceId,
            RollerSpeed = context.RollerSpeed,
            StartedAt = DateTimeOffset.UtcNow.ToString("O"),
        };
        await File.WriteAllTextAsync(
            Path.Combine(sessionDirectory, "line-scan-meta.json"),
            JsonSerializer.Serialize(meta, JsonOptions),
            ct).ConfigureAwait(false);
        await _host.StartLineAsync(
            sessionDirectory,
            context.ScannerId,
            context.RollerSpeed,
            context.TraceId,
            ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task StopRecordingAsync(string sessionDirectory, CancellationToken ct = default)
    {
        var taskId = SessionTaskId(sessionDirectory);
        try
        {
            await _host.FinalizeLineAsync(taskId, ct).ConfigureAwait(false);
        }
        catch (LineScanDeviceException ex)
        {
            // Keep infrastructure exceptions behind the domain scanner boundary,
            // as the setup/profile facade already does.
            throw new ScanTaskException(400, ex.Code, ex.Message);
        }
    }

    /// <inheritdoc />
    public Task AbortRecordingAsync(string sessionDirectory, CancellationToken ct = default)
    {
        var taskId = SessionTaskId(sessionDirectory);
        return _host.AbortLineAsync(taskId, ct);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MeasuredAngleIron>> MeasureAsync(
        string sessionDirectory,
        RollerBaselineRecord baseline,
        double rollerSpeedMmPerSec,
        double gapMm,
        double minHeightMm,
        CancellationToken ct = default)
    {
        _ = baseline;
        _ = rollerSpeedMmPerSec;
        _ = gapMm;
        _ = minHeightMm;
        var metaPath = Path.Combine(sessionDirectory, "line-scan-meta.json");
        if (!File.Exists(metaPath))
        {
            throw new InvalidDataException("找不到 line-scan-meta.json，無法量測。");
        }

        var meta = JsonSerializer.Deserialize<LineScanSessionMeta>(await File.ReadAllTextAsync(metaPath, ct).ConfigureAwait(false), JsonOptions)
            ?? throw new InvalidDataException("line-scan-meta.json 無法讀取。");
        var calibration = LineScanMeasurementContext.ReadCapturedCalibration(sessionDirectory)
            ?? await _resolver.GetCalibrationAsync(meta.ScannerId, ct).ConfigureAwait(false);
        _ = ResolveCompletedOutputPath(sessionDirectory);
        // Measurement needs the recorded near-table context that export height
        // filtering omits. Its frozen capture calibration/settings take priority.
        var context = await Task.Run(() => LineScanMeasurementContext.Load(sessionDirectory, calibration, ct), ct)
            .ConfigureAwait(false);
        return _measure.MeasureLine(context.Frames, context.Calibration, ct);
    }

    internal static string ResolveCompletedOutputPath(string sessionDirectory)
    {
        string capturePath = Path.Combine(sessionDirectory, "line_raw.capture.json");
        if (File.Exists(capturePath))
        {
            using var capture = JsonDocument.Parse(File.ReadAllText(capturePath));
            if (capture.RootElement.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("錄製設定檔必須是 JSON 物件。");
            if (capture.RootElement.TryGetProperty("output_3d", out var output)
                && output.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(output.GetString()))
            {
                string recorded = output.GetString()!;
                string resolved = Path.IsPathRooted(recorded) ? recorded : Path.Combine(sessionDirectory, recorded);
                if (File.Exists(resolved)) return Path.GetFullPath(resolved);
                string relocated = Path.Combine(sessionDirectory, Path.GetFileName(recorded));
                if (File.Exists(relocated)) return Path.GetFullPath(relocated);
            }
        }
        foreach (string file in new[] { "line_3d.ply", "line_raw_3d.ply" })
        {
            string path = Path.Combine(sessionDirectory, file);
            if (File.Exists(path)) return Path.GetFullPath(path);
        }
        throw new InvalidDataException("尚未完成 3D 重建，找不到錄製設定記載的 3D PLY。");
    }

    private static string SessionTaskId(string sessionDirectory)
    {
        return Path.GetFileName(sessionDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
    }

    private SessionContext ReadSessionContext(string sessionDirectory, string ip, int port)
    {
        var taskId = SessionTaskId(sessionDirectory);
        var taskPath = Path.Combine(sessionDirectory, "task.json");
        if (File.Exists(taskPath))
        {
            var doc = JsonSerializer.Deserialize<TaskDocument>(File.ReadAllText(taskPath), JsonDefaults.Options);
            if (doc is not null && !string.IsNullOrWhiteSpace(doc.ScannerId))
            {
                return new SessionContext(
                    doc.ScannerId.Trim(),
                    taskId,
                    doc.TraceId,
                    doc.RollerSpeed ?? 0);
            }
        }

        var scannerId = ResolveScannerId(ip, port);
        if (scannerId is null)
        {
            throw new LineScanDeviceException("CAL_CFG_001", "無法由 IP／埠對應線掃描 scannerId。");
        }

        return new SessionContext(scannerId, taskId, null, 0);
    }

    private string? ResolveScannerId(string ip, int port)
    {
        foreach (var pair in _registry)
        {
            if (string.Equals(pair.Value.Ip?.Trim(), ip.Trim(), StringComparison.OrdinalIgnoreCase) && pair.Value.Port == port)
            {
                return pair.Key;
            }
        }

        return null;
    }

    private static RollerSampleResult Failed(string code, string message)
    {
        return new RollerSampleResult
        {
            Clear = false,
            Code = code,
            Message = message,
        };
    }

    private sealed class LineScanSessionMeta
    {
        public string ScannerId { get; set; } = "";

        public string TaskId { get; set; } = "";

        public string? TraceId { get; set; }

        public double RollerSpeed { get; set; }

        public string StartedAt { get; set; } = "";
    }

    private readonly record struct SessionContext(string ScannerId, string TaskId, string? TraceId, double RollerSpeed);
}
