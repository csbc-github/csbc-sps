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
    public Task StopRecordingAsync(string sessionDirectory, CancellationToken ct = default)
    {
        var taskId = SessionTaskId(sessionDirectory);
        return _host.FinalizeLineAsync(taskId, ct);
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
        var calibration = await _resolver.GetCalibrationAsync(meta.ScannerId, ct).ConfigureAwait(false);
        var outputPath = Path.Combine(sessionDirectory, "line_raw_3d.ply");
        if (!File.Exists(outputPath))
        {
            throw new InvalidDataException("尚未完成 3D 重建，找不到 line_raw_3d.ply。");
        }

        var cloud = await Task.Run(() => Cloud.Read(outputPath), ct).ConfigureAwait(false);
        return _measure.MeasureLine(cloud.FullData(), calibration, ct);
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
            var doc = JsonSerializer.Deserialize<TaskDocument>(File.ReadAllText(taskPath), JsonOptions);
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
