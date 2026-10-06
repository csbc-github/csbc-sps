using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using ThreeDScan.Domain.Abstractions;
using ThreeDScan.Domain.Models;

namespace ThreeDScan.Infrastructure.LineScan;

/// <summary>呼叫 line_scan_worker.py。錄製連到 IP_control 的 sick_lidar。</summary>
public sealed class SickPythonLineScanner : ISickLineScanner
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private readonly LineScanSettings _settings;
    private readonly ConcurrentDictionary<string, Process> _recorders =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>建立 Python 線掃描執行器。</summary>
    public SickPythonLineScanner(IOptions<LineScanSettings> settings)
    {
        _settings = settings.Value ?? new LineScanSettings();
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
        var directory = Path.Combine(Path.GetTempPath(), "threedscan-roller-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var output = Path.Combine(directory, "roller.json");
        try
        {
            var exit = await RunAsync(
                new[]
                {
                    ScriptPath(),
                    "--ip-control",
                    RequireIpControl(),
                    "calibrate",
                    "--ip",
                    ip,
                    "--port",
                    port.ToString(),
                    "--frames",
                    Math.Max(1, frames).ToString(),
                    "--tolerance",
                    toleranceMm.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    "--range-deviation",
                    rangeDeviationMm.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    "--out",
                    output,
                },
                directory,
                TimeSpan.FromSeconds(40),
                ct).ConfigureAwait(false);
            if (!File.Exists(output))
            {
                return Failed("CAL_SYS_001", string.IsNullOrWhiteSpace(exit) ? "滾輪校正沒有輸出。" : exit);
            }

            var payload = JsonSerializer.Deserialize<CalibrateFile>(await File.ReadAllTextAsync(output, ct).ConfigureAwait(false), JsonOptions);
            if (payload is null)
            {
                return Failed("CAL_SYS_001", "滾輪校正輸出無法讀取。");
            }

            if (!payload.Ok)
            {
                return new RollerSampleResult
                {
                    Clear = false,
                    Code = string.IsNullOrWhiteSpace(payload.Code) ? "CAL_LINE_004" : payload.Code,
                    Message = string.IsNullOrWhiteSpace(payload.Message) ? "輪廓變異超過容許值，判定滾輪尚未清空" : payload.Message,
                    ZDistance = payload.ZDistance,
                    FlatnessMm = payload.FlatnessMm,
                    LineA = payload.LineA,
                    LineB = payload.LineB,
                    LineC = payload.LineC,
                };
            }

            return new RollerSampleResult
            {
                Clear = true,
                ZDistance = payload.ZDistance,
                FlatnessMm = payload.FlatnessMm,
                LineA = payload.LineA,
                LineB = payload.LineB,
                LineC = payload.LineC,
            };
        }
        catch (Exception ex) when (ex is InvalidOperationException or TimeoutException or IOException)
        {
            return Failed("CAL_SYS_001", ex.Message);
        }
        finally
        {
            TryDelete(directory);
        }
    }

    /// <inheritdoc />
    public async Task StartRecordingAsync(string sessionDirectory, string ip, int port, CancellationToken ct = default)
    {
        Directory.CreateDirectory(sessionDirectory);
        var process = StartProcess(
            new[]
            {
                ScriptPath(),
                "--ip-control",
                RequireIpControl(),
                "record",
                "--ip",
                ip,
                "--port",
                port.ToString(),
                "--session",
                sessionDirectory,
            },
            sessionDirectory,
            new StringBuilder());
        _recorders[sessionDirectory] = process;
        var ready = Path.Combine(sessionDirectory, "ready");
        var failed = Path.Combine(sessionDirectory, "failed");
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            if (File.Exists(failed))
            {
                var message = await File.ReadAllTextAsync(failed, ct).ConfigureAwait(false);
                await AbortRecordingAsync(sessionDirectory, ct).ConfigureAwait(false);
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(message) ? "光達沒有開始輸出截面。" : message.Trim());
            }

            if (File.Exists(ready))
            {
                return;
            }

            if (process.HasExited)
            {
                throw new InvalidOperationException("光達錄製程序已結束，尚未收到截面。");
            }

            await Task.Delay(100, ct).ConfigureAwait(false);
        }

        await AbortRecordingAsync(sessionDirectory, CancellationToken.None).ConfigureAwait(false);
        throw new TimeoutException("等待光達截面逾時。");
    }

    /// <inheritdoc />
    public async Task StopRecordingAsync(string sessionDirectory, CancellationToken ct = default)
    {
        if (!_recorders.TryGetValue(sessionDirectory, out var process))
        {
            return;
        }

        var stop = Path.Combine(sessionDirectory, "STOP");
        await File.WriteAllTextAsync(stop, "stop", ct).ConfigureAwait(false);
        var closed = Path.Combine(sessionDirectory, "closed");
        var deadline = DateTime.UtcNow.AddSeconds(8);
        while (DateTime.UtcNow < deadline && !File.Exists(closed) && !process.HasExited)
        {
            await Task.Delay(100, ct).ConfigureAwait(false);
        }

        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
        }

        _recorders.TryRemove(sessionDirectory, out _);
        process.Dispose();
    }

    /// <inheritdoc />
    public Task AbortRecordingAsync(string sessionDirectory, CancellationToken ct = default)
    {
        _ = ct;
        if (_recorders.TryRemove(sessionDirectory, out var process))
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (InvalidOperationException)
            {
            }

            process.Dispose();
        }

        return Task.CompletedTask;
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
        var baselinePath = Path.Combine(sessionDirectory, "baseline.json");
        var output = Path.Combine(sessionDirectory, "parts.json");
        await File.WriteAllTextAsync(
            baselinePath,
            JsonSerializer.Serialize(new
            {
                lineA = baseline.LineA,
                lineB = baseline.LineB,
                lineC = baseline.LineC,
            }, JsonOptions),
            ct).ConfigureAwait(false);
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        var exit = await RunAsync(
            new[]
            {
                ScriptPath(),
                "--ip-control",
                RequireIpControl(),
                "measure",
                "--session",
                sessionDirectory,
                "--baseline",
                baselinePath,
                "--speed",
                rollerSpeedMmPerSec.ToString(culture),
                "--gap",
                gapMm.ToString(culture),
                "--min-height",
                minHeightMm.ToString(culture),
                "--out",
                output,
            },
            sessionDirectory,
            TimeSpan.FromMinutes(10),
            ct).ConfigureAwait(false);
        if (!File.Exists(output))
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(exit) ? "尺寸整理沒有輸出。" : exit);
        }

        var payload = JsonSerializer.Deserialize<MeasureFile>(await File.ReadAllTextAsync(output, ct).ConfigureAwait(false), JsonOptions);
        if (payload?.Parts is null)
        {
            throw new InvalidOperationException("尺寸整理輸出無法讀取。");
        }

        var parts = new List<MeasuredAngleIron>();
        foreach (var part in payload.Parts)
        {
            parts.Add(new MeasuredAngleIron
            {
                Index = part.Index,
                HeadLeft = ToPoint(part.Head.Left),
                HeadRight = ToPoint(part.Head.Right),
                HeadApex = ToPoint(part.Head.Apex),
                TailLeft = ToPoint(part.Tail.Left),
                TailRight = ToPoint(part.Tail.Right),
                TailApex = ToPoint(part.Tail.Apex),
                Width = part.Width,
                Height = part.Height,
                Length = part.LeftLength,
                LeftLength = part.LeftLength,
                RightLength = part.RightLength,
            });
        }

        return parts;
    }

    private async Task<string> RunAsync(IReadOnlyList<string> args, string workingDirectory, TimeSpan timeout, CancellationToken ct)
    {
        var errors = new StringBuilder();
        using var process = StartProcess(args, workingDirectory, errors);
        using var cancel = ct.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (InvalidOperationException)
            {
            }
        });
        var finished = await Task.Run(() => process.WaitForExit((int)timeout.TotalMilliseconds), ct).ConfigureAwait(false);
        if (!finished)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("Python 線掃描程序逾時。");
        }

        process.WaitForExit();
        if (process.ExitCode == 0)
        {
            return "";
        }

        return string.IsNullOrWhiteSpace(errors.ToString())
            ? "Python 結束碼 " + process.ExitCode
            : errors.ToString().Trim();
    }

    private Process StartProcess(IReadOnlyList<string> args, string workingDirectory, StringBuilder errors)
    {
        var start = new ProcessStartInfo
        {
            FileName = string.IsNullOrWhiteSpace(_settings.PythonExe) ? "python" : _settings.PythonExe,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        start.Environment["PYTHONIOENCODING"] = "utf-8";
        foreach (var arg in args)
        {
            start.ArgumentList.Add(arg);
        }

        var process = new Process
        {
            StartInfo = start,
            EnableRaisingEvents = true,
        };
        process.ErrorDataReceived += (_, eventArgs) =>
        {
            if (!string.IsNullOrWhiteSpace(eventArgs.Data))
            {
                errors.AppendLine(eventArgs.Data);
            }
        };
        process.OutputDataReceived += (_, _) =>
        {
        };
        if (!process.Start())
        {
            throw new InvalidOperationException("無法啟動 Python。");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return process;
    }

    private string ScriptPath()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "LineScan", "line_scan_worker.py");
        if (!File.Exists(path))
        {
            throw new InvalidOperationException("找不到線掃描工作程序：" + path);
        }

        return path;
    }

    private string RequireIpControl()
    {
        if (string.IsNullOrWhiteSpace(_settings.IpControlRoot) || !Directory.Exists(_settings.IpControlRoot))
        {
            throw new InvalidOperationException("請在 LineScan:IpControlRoot 設定 IP_control 資料夾。");
        }

        return _settings.IpControlRoot;
    }

    private static MeasuredPoint ToPoint(PointFile point)
    {
        return new MeasuredPoint
        {
            X = point.X,
            Y = point.Y,
            Z = point.Z,
        };
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

    private static void TryDelete(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
        catch (IOException)
        {
        }
    }

    private sealed class CalibrateFile
    {
        public bool Ok { get; set; }

        public string Code { get; set; } = "";

        public string Message { get; set; } = "";

        public double ZDistance { get; set; }

        public double FlatnessMm { get; set; }

        public double LineA { get; set; }

        public double LineB { get; set; }

        public double LineC { get; set; }
    }

    private sealed class MeasureFile
    {
        public List<PartFile> Parts { get; set; } = new();
    }

    private sealed class PartFile
    {
        public int Index { get; set; }

        public CornerFile Head { get; set; } = new();

        public CornerFile Tail { get; set; } = new();

        public double Width { get; set; }

        public double Height { get; set; }

        public double LeftLength { get; set; }

        public double RightLength { get; set; }
    }

    private sealed class CornerFile
    {
        public PointFile Left { get; set; } = new();

        public PointFile Right { get; set; } = new();

        public PointFile Apex { get; set; } = new();
    }

    private sealed class PointFile
    {
        public double X { get; set; }

        public double Y { get; set; }

        public double Z { get; set; }
    }
}
