using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows.Input;
using Microsoft.Win32;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using ThreeDScan.Common;
using ThreeDScan.Models;

namespace ThreeDScan.ViewModels;

// 噴印區 VM：對應 SA UC-04 面掃描（噴印／割切區）／ SD §10.3
public class PrintingViewModel : ViewModelBase
{
    private const string LidarIp = "192.168.0.1";
    private const int LidarPort = 24691;
    private const string CliExeName = "Ljs8SurfaceScanCli.exe";
    /// <summary>WPF 端效能上限；Python 預設 2e6，此處縮小但流程順序與著色與 viewer 一致。</summary>
    private const int MaxDisplayPoints = 60_000;

    private bool _is3DMode = true;
    private CoordinateInfo _machineCoordinate = new(245.320, 128.550, 32.180);
    private CoordinateInfo _workpieceCoordinate = new(120.150, 85.220, 28.450);
    private bool _isScanning;
    private string _scanStatusText = "就緒 — 等待面掃描";
    private SystemStatus _overallStatus = SystemStatus.Normal;
    private string _lastPlyPath = "";
    private IReadOnlyList<PointCloudRenderVertex> _pointCloudVertices = Array.Empty<PointCloudRenderVertex>();
    private CancellationTokenSource? _scanCts;
    private Process? _runningProcess;

    public bool Is2DMode { get => !_is3DMode; set { if (SetProperty(ref _is3DMode, !value)) OnPropertyChanged(nameof(Is3DMode)); } }
    public bool Is3DMode { get => _is3DMode; set { if (SetProperty(ref _is3DMode, value)) OnPropertyChanged(nameof(Is2DMode)); } }

    public CoordinateInfo MachineCoordinate { get => _machineCoordinate; set => SetProperty(ref _machineCoordinate, value); }
    public CoordinateInfo WorkpieceCoordinate { get => _workpieceCoordinate; set => SetProperty(ref _workpieceCoordinate, value); }
    public bool IsScanning
    {
        get => _isScanning;
        set
        {
            if (SetProperty(ref _isScanning, value))
                CommandManager.InvalidateRequerySuggested();
        }
    }
    public string ScanStatusText { get => _scanStatusText; set => SetProperty(ref _scanStatusText, value); }
    public SystemStatus OverallStatus { get => _overallStatus; set => SetProperty(ref _overallStatus, value); }
    public string LastPlyPath { get => _lastPlyPath; set => SetProperty(ref _lastPlyPath, value); }
    public IReadOnlyList<PointCloudRenderVertex> PointCloudVertices { get => _pointCloudVertices; set => SetProperty(ref _pointCloudVertices, value); }

    public ICommand StartScanCommand { get; }
    public ICommand StopScanCommand { get; }
    public ICommand TestLoadPointCloudCommand { get; }

    public PrintingViewModel()
    {
        StartScanCommand = new RelayCommand(_ => _ = StartAsync(), _ => !IsScanning);
        StopScanCommand = new RelayCommand(_ => _ = StopAsync(), _ => IsScanning);
        TestLoadPointCloudCommand = new RelayCommand(_ => _ = TestLoadPointCloudAsync(), _ => !IsScanning);
    }

    /// <summary>測試用：座標直接依檔案 x,y,z（不做 Z 去底／體素／隨機抽樣）；著色依範圍最大軸做彩虹以利辨識。</summary>
    private async Task TestLoadPointCloudAsync()
    {
        var dlg = new OpenFileDialog
        {
            Title = "測試：載入 3D 點雲檔",
            Filter = "PLY 點雲 (*.ply)|*.ply|所有檔案 (*.*)|*.*",
            CheckFileExists = true
        };
        if (dlg.ShowDialog() != true) return;

        var path = dlg.FileName;
        LastPlyPath = path;
        ScanStatusText = "測試載入（原始座標）…";
        OverallStatus = SystemStatus.Warning;
        Is3DMode = true;

        try
        {
            PointCloudVertices = await LoadPointCloudRawAsync(path, CancellationToken.None);
            if (PointCloudVertices.Count > 0)
            {
                ScanStatusText = $"測試載入（原始）：{Path.GetFileName(path)}（{PointCloudVertices.Count:N0} 點）";
                OverallStatus = SystemStatus.Normal;
            }
            else
            {
                ScanStatusText = $"無法顯示（需 ASCII PLY 含 x,y,z）：{Path.GetFileName(path)}";
                OverallStatus = SystemStatus.Error;
            }
        }
        catch (Exception ex)
        {
            ScanStatusText = "測試載入失敗：" + ex.Message;
            OverallStatus = SystemStatus.Error;
        }
    }

    private async Task StartAsync()
    {
        if (IsScanning) return;

        if (!TryResolveCliExecutable(out var cliExePath))
        {
            ScanStatusText = "找不到 Ljs8SurfaceScanCli.exe（請確認 dist/disT 位置）";
            OverallStatus = SystemStatus.Error;
            return;
        }

        var outputDir = Path.Combine(AppContext.BaseDirectory, "ScanOutput");
        Directory.CreateDirectory(outputDir);
        var outPlyPath = Path.Combine(outputDir, $"print_scan_{DateTime.Now:yyyyMMdd_HHmmss}.ply");

        var startInfo = new ProcessStartInfo
        {
            FileName = cliExePath,
            WorkingDirectory = Path.GetDirectoryName(cliExePath) ?? AppContext.BaseDirectory,
            Arguments = BuildCliArguments(outPlyPath),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        _scanCts = new CancellationTokenSource();
        _runningProcess = new Process { StartInfo = startInfo };

        ScanStatusText = "掃描中（呼叫 Python CLI）…";
        OverallStatus = SystemStatus.Warning;

        try
        {
            IsScanning = true;
            if (!_runningProcess.Start())
            {
                ScanStatusText = "啟動掃描程式失敗";
                OverallStatus = SystemStatus.Error;
                return;
            }

            var stdoutReadTask = _runningProcess.StandardOutput.ReadToEndAsync(_scanCts.Token);
            var stderrReadTask = _runningProcess.StandardError.ReadToEndAsync(_scanCts.Token);
            await _runningProcess.WaitForExitAsync(_scanCts.Token);
            var stdout = await stdoutReadTask;
            var stderr = await stderrReadTask;

            if (_scanCts.IsCancellationRequested)
            {
                ScanStatusText = "已停止";
                OverallStatus = SystemStatus.Normal;
                return;
            }

            if (_runningProcess.ExitCode == 0)
            {
                var result = TryParseCliResult(stdout);
                if (result is { Ok: true })
                {
                    LastPlyPath = ResolvePlyPath(result.PlyPath, outPlyPath, startInfo.WorkingDirectory);
                    ScanStatusText = "掃描完成，載入點雲中…";
                    OverallStatus = SystemStatus.Normal;
                    Is3DMode = true;
                    PointCloudVertices = await LoadPointCloudAsync(LastPlyPath, _scanCts.Token);
                    ScanStatusText = PointCloudVertices.Count > 0
                        ? $"掃描完成：{Path.GetFileName(LastPlyPath)}（顯示 {PointCloudVertices.Count:N0} 點）"
                        : $"掃描完成但無可顯示點雲：{Path.GetFileName(LastPlyPath)}";
                    return;
                }
            }

            var fail = TryParseCliResult(stdout);
            var message = fail is null
                ? $"掃描失敗（ExitCode={_runningProcess.ExitCode}）"
                : BuildFailureMessage(fail);
            if (!string.IsNullOrWhiteSpace(stderr))
            {
                message = $"{message} / {stderr.Trim()}";
            }
            ScanStatusText = message;
            OverallStatus = SystemStatus.Error;
        }
        catch (OperationCanceledException)
        {
            ScanStatusText = "已停止";
            OverallStatus = SystemStatus.Normal;
        }
        catch (Exception ex)
        {
            ScanStatusText = "掃描異常：" + ex.Message;
            OverallStatus = SystemStatus.Error;
        }
        finally
        {
            try
            {
                if (_runningProcess is { HasExited: false })
                {
                    _runningProcess.Kill(entireProcessTree: true);
                    await _runningProcess.WaitForExitAsync();
                }
            }
            catch
            {
                // Ignore cleanup errors.
            }

            _runningProcess?.Dispose();
            _runningProcess = null;
            _scanCts?.Dispose();
            _scanCts = null;
            IsScanning = false;
        }
    }

    private async Task StopAsync()
    {
        if (!IsScanning) return;
        ScanStatusText = "停止中…";
        _scanCts?.Cancel();
        await Task.CompletedTask;
    }

    private static string BuildCliArguments(string outPlyPath)
        => $"--ip {LidarIp} --port {LidarPort} --out-ply {QuoteArgument(outPlyPath)}";

    private static string QuoteArgument(string value)
        => "\"" + value.Replace("\"", "\\\"") + "\"";

    private static ScanCliResult? TryParseCliResult(string stdout)
    {
        if (string.IsNullOrWhiteSpace(stdout)) return null;
        try
        {
            return JsonSerializer.Deserialize<ScanCliResult>(stdout.Trim(), new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch
        {
            return null;
        }
    }

    private static string BuildFailureMessage(ScanCliResult fail)
    {
        if (!string.IsNullOrWhiteSpace(fail.Error))
            return "掃描失敗：" + fail.Error;
        if (!string.IsNullOrWhiteSpace(fail.Step) && fail.Rc.HasValue)
            return $"掃描失敗：{fail.Step} (rc={fail.Rc.Value})";
        if (!string.IsNullOrWhiteSpace(fail.Step))
            return $"掃描失敗：{fail.Step}";
        return "掃描失敗";
    }

    private static bool TryResolveCliExecutable(out string exePath)
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "dist", "Ljs8SurfaceScanCli", CliExeName),
            Path.Combine(AppContext.BaseDirectory, "disT", "Ljs8SurfaceScanCli", CliExeName),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "dist", "Ljs8SurfaceScanCli", CliExeName),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "disT", "Ljs8SurfaceScanCli", CliExeName),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "dist", "Ljs8SurfaceScanCli", CliExeName),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "disT", "Ljs8SurfaceScanCli", CliExeName)
        };

        foreach (var candidate in candidates)
        {
            var fullPath = Path.GetFullPath(candidate);
            if (File.Exists(fullPath))
            {
                exePath = fullPath;
                return true;
            }
        }

        exePath = string.Empty;
        return false;
    }

    /// <summary>
    /// 測試載入：座標不 flatten／不 voxel／不隨機抽樣；點數超過上限時依檔案順序等距跳點。
    /// 著色仍用與 viewer 相同之 HSV 彩虹，但依 X/Y/Z 中「範圍最大」的軸上色（通常 Z 為高度；若 Z 幾乎常數則自動改看 X 或 Y），避免整朵雲變成單色像灰階。
    /// </summary>
    private static async Task<IReadOnlyList<PointCloudRenderVertex>> LoadPointCloudRawAsync(string plyPath, CancellationToken token)
    {
        return await Task.Run(() =>
        {
            if (!File.Exists(plyPath) ||
                !TryParseAsciiPlyPoints(plyPath, token, out var raw) ||
                raw.Count == 0)
            {
                return (IReadOnlyList<PointCloudRenderVertex>)Array.Empty<PointCloudRenderVertex>();
            }

            int n = raw.Count;
            int step = 1;
            if (MaxDisplayPoints > 0 && n > MaxDisplayPoints)
            {
                step = (n + MaxDisplayPoints - 1) / MaxDisplayPoints;
            }

            var picked = new List<(double X, double Y, double Z)>(Math.Min(n, MaxDisplayPoints > 0 ? MaxDisplayPoints : n));
            for (int i = 0; i < n; i += step)
            {
                token.ThrowIfCancellationRequested();
                picked.Add(raw[i]);
                if (MaxDisplayPoints > 0 && picked.Count >= MaxDisplayPoints) break;
            }

            ComputeBounds(picked, out var minX, out var maxX, out var minY, out var maxY, out var minZ, out var maxZ);

            var result = new List<PointCloudRenderVertex>(picked.Count);
            foreach (var p in picked)
            {
                var c = RainbowColorForVertex(p.X, p.Y, p.Z, minX, maxX, minY, maxY, minZ, maxZ);
                result.Add(new PointCloudRenderVertex(new Point3D(p.X, p.Y, p.Z), c));
            }

            return (IReadOnlyList<PointCloudRenderVertex>)result;
        }, token);
    }

    private static async Task<IReadOnlyList<PointCloudRenderVertex>> LoadPointCloudAsync(string plyPath, CancellationToken token)
    {
        return await Task.Run(() =>
        {
            if (!File.Exists(plyPath))
            {
                return (IReadOnlyList<PointCloudRenderVertex>)Array.Empty<PointCloudRenderVertex>();
            }

            if (!TryParseAsciiPlyPoints(plyPath, token, out var raw) || raw.Count == 0)
            {
                return (IReadOnlyList<PointCloudRenderVertex>)Array.Empty<PointCloudRenderVertex>();
            }

            // 對齊 point_cloud_viewer.prepare_point_cloud：flatten_z → voxel(0) → max_points 隨機抽樣 → 依 Z 著色
            PreparePointCloudInPlace(raw, flattenZ: true, voxelSize: 0, maxPoints: MaxDisplayPoints, token);

            ComputeBounds(raw, out var minX, out var maxX, out var minY, out var maxY, out var minZ, out var maxZ);

            var result = new List<PointCloudRenderVertex>(raw.Count);
            foreach (var p in raw)
            {
                // 仍以 Z 為主；若去底後高度差極小（看起來像灰階）則改用最長軸著色
                var c = RainbowColorForVertex(p.X, p.Y, p.Z, minX, maxX, minY, maxY, minZ, maxZ);
                result.Add(new PointCloudRenderVertex(new Point3D(p.X, p.Y, p.Z), c));
            }

            return result;
        }, token);
    }

    private static bool TryParseAsciiPlyPoints(string plyPath, CancellationToken token, out List<(double X, double Y, double Z)> points)
    {
        points = new List<(double, double, double)>();
        bool isAscii = false;
        bool inHeader = true;
        int vertexCount = 0;
        int dataIndex = 0;

        foreach (var rawLine in File.ReadLines(plyPath))
        {
            token.ThrowIfCancellationRequested();
            var line = rawLine.Trim();
            if (line.Length == 0) continue;

            if (inHeader)
            {
                if (line.StartsWith("format ", StringComparison.OrdinalIgnoreCase))
                {
                    isAscii = line.Contains("ascii", StringComparison.OrdinalIgnoreCase);
                }
                else if (line.StartsWith("element vertex ", StringComparison.OrdinalIgnoreCase))
                {
                    var countText = line["element vertex ".Length..].Trim();
                    if (int.TryParse(countText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) && count > 0)
                    {
                        vertexCount = count;
                    }
                }
                else if (line.Equals("end_header", StringComparison.OrdinalIgnoreCase))
                {
                    inHeader = false;
                    if (!isAscii)
                    {
                        points = new List<(double, double, double)>();
                        return false;
                    }
                }

                continue;
            }

            if (vertexCount > 0 && dataIndex >= vertexCount) break;
            dataIndex++;

            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3) continue;

            if (double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) &&
                double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y) &&
                double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var z))
            {
                points.Add((x, y, z));
            }
        }

        return points.Count > 0;
    }

    private static void PreparePointCloudInPlace(
        List<(double X, double Y, double Z)> pts,
        bool flattenZ,
        double voxelSize,
        int maxPoints,
        CancellationToken token)
    {
        if (pts.Count == 0) return;

        if (flattenZ)
        {
            double zMin = double.MaxValue;
            foreach (var p in pts)
            {
                if (p.Z < zMin) zMin = p.Z;
            }
            for (int i = 0; i < pts.Count; i++)
            {
                var p = pts[i];
                pts[i] = (p.X, p.Y, p.Z - zMin);
            }
        }

        if (voxelSize > 0)
        {
            VoxelDownsampleInPlace(pts, voxelSize, token);
        }

        int n = pts.Count;
        if (maxPoints > 0 && n > maxPoints)
        {
            var indices = new int[n];
            for (int i = 0; i < n; i++) indices[i] = i;
            Random.Shared.Shuffle(indices.AsSpan());
            var picked = new List<(double, double, double)>(maxPoints);
            for (int i = 0; i < maxPoints; i++)
            {
                token.ThrowIfCancellationRequested();
                picked.Add(pts[indices[i]]);
            }
            pts.Clear();
            pts.AddRange(picked);
        }
    }

    private static void VoxelDownsampleInPlace(List<(double X, double Y, double Z)> pts, double voxelSize, CancellationToken token)
    {
        if (voxelSize <= 0 || pts.Count == 0) return;
        var inv = 1.0 / voxelSize;
        var seen = new HashSet<(long, long, long)>();
        var kept = new List<(double, double, double)>();
        foreach (var p in pts)
        {
            token.ThrowIfCancellationRequested();
            var kx = (long)Math.Floor(p.X * inv);
            var ky = (long)Math.Floor(p.Y * inv);
            var kz = (long)Math.Floor(p.Z * inv);
            if (seen.Add((kx, ky, kz)))
            {
                kept.Add(p);
            }
        }
        pts.Clear();
        pts.AddRange(kept);
    }

    private static void ComputeBounds(
        IReadOnlyList<(double X, double Y, double Z)> pts,
        out double minX, out double maxX, out double minY, out double maxY, out double minZ, out double maxZ)
    {
        minX = minY = minZ = double.MaxValue;
        maxX = maxY = maxZ = double.MinValue;
        foreach (var p in pts)
        {
            if (p.X < minX) minX = p.X;
            if (p.X > maxX) maxX = p.X;
            if (p.Y < minY) minY = p.Y;
            if (p.Y > maxY) maxY = p.Y;
            if (p.Z < minZ) minZ = p.Z;
            if (p.Z > maxZ) maxZ = p.Z;
        }
    }

    /// <summary>
    /// 以 X/Y/Z 中「數值範圍最大」的軸做彩虹著色（多為 Z＝高度）；若 Z 幾乎沒變化則自動改看 X 或 Y，避免整片像灰階。
    /// </summary>
    private static Color RainbowColorForVertex(
        double x, double y, double z,
        double minX, double maxX, double minY, double maxY, double minZ, double maxZ)
    {
        var sx = maxX - minX;
        var sy = maxY - minY;
        var sz = maxZ - minZ;
        if (sz >= sx && sz >= sy)
        {
            return HeightToRgbColor(z, minZ, maxZ);
        }
        if (sy >= sx)
        {
            return HeightToRgbColor(y, minY, maxY);
        }
        return HeightToRgbColor(x, minX, maxX);
    }

    /// <summary>對齊 point_cloud_viewer._z_to_rgb_colors（colorsys.hsv_to_rgb, S=0.88, V=0.96）。</summary>
    private static Color HeightToRgbColor(double v, double lo, double hi)
    {
        var span = Math.Max(hi - lo, 1e-12);
        var t = (v - lo) / span;
        if (t < 0) t = 0;
        else if (t > 1) t = 1;
        const int levels = 220;
        t = Math.Round(t * (levels - 1)) / (levels - 1);
        var h = 0.75 * (1.0 - t);
        HsvToRgb(h, 0.88, 0.96, out var r, out var g, out var b);
        return Color.FromRgb(
            (byte)Math.Clamp((int)(r * 255.0), 0, 255),
            (byte)Math.Clamp((int)(g * 255.0), 0, 255),
            (byte)Math.Clamp((int)(b * 255.0), 0, 255));
    }

    private static void HsvToRgb(double h, double s, double v, out double r, out double g, out double b)
    {
        if (s <= 0)
        {
            r = g = b = v;
            return;
        }

        var hh = h * 6.0;
        var i = (int)Math.Floor(hh);
        var f = hh - i;
        var p = v * (1.0 - s);
        var q = v * (1.0 - s * f);
        var t = v * (1.0 - s * (1.0 - f));
        switch (i % 6)
        {
            case 0: r = v; g = t; b = p; break;
            case 1: r = q; g = v; b = p; break;
            case 2: r = p; g = v; b = t; break;
            case 3: r = p; g = q; b = v; break;
            case 4: r = t; g = p; b = v; break;
            default: r = v; g = p; b = q; break;
        }
    }

    private static string ResolvePlyPath(string? cliPlyPath, string fallbackPath, string cliWorkingDirectory)
    {
        if (!string.IsNullOrWhiteSpace(cliPlyPath))
        {
            var candidate = cliPlyPath.Trim().Trim('"');
            var full = Path.IsPathRooted(candidate)
                ? candidate
                : Path.Combine(cliWorkingDirectory, candidate);
            full = Path.GetFullPath(full);
            if (File.Exists(full))
            {
                return full;
            }
        }

        if (File.Exists(fallbackPath))
        {
            return fallbackPath;
        }

        var outputDir = Path.GetDirectoryName(fallbackPath);
        if (!string.IsNullOrWhiteSpace(outputDir) && Directory.Exists(outputDir))
        {
            var latest = new DirectoryInfo(outputDir)
                .GetFiles("*.ply", SearchOption.TopDirectoryOnly)
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .FirstOrDefault();
            if (latest is not null)
            {
                return latest.FullName;
            }
        }

        return fallbackPath;
    }

    private sealed class ScanCliResult
    {
        public bool Ok { get; set; }
        public string? PlyPath { get; set; }
        public string? Step { get; set; }
        public int? Rc { get; set; }
        public string? Error { get; set; }
    }
}
