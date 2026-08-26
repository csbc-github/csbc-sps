using System.Globalization;
using System.IO;
using System.Windows.Input;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using ThreeDScan.Common;
using ThreeDScan.Domain.Abstractions;
using ThreeDScan.Models;

namespace ThreeDScan.ViewModels;

// 噴印區 VM：對應 SA UC-04 面掃描（噴印／割切區）／ SD §10.3
public class PrintingViewModel : ViewModelBase
{
    /// <summary>WPF 端效能上限；Python 預設 2e6，此處縮小但流程順序與著色與 viewer 一致。</summary>
    private const int MaxDisplayPoints = 60_000;

    private readonly IConfiguration _config;

    private enum PreviewMode
    {
        Height2D,
        PointCloud,
        Glb,
    }

    private PreviewMode _previewMode = PreviewMode.PointCloud;
    private CoordinateInfo _machineCoordinate = new(245.320, 128.550, 32.180);
    private CoordinateInfo _workpieceCoordinate = new(120.150, 85.220, 28.450);
    private bool _isScanning;
    private string _scanStatusText = "就緒 — 等待面掃描";
    private SystemStatus _overallStatus = SystemStatus.Normal;
    private string _lastPlyPath = "";
    private string? _glbPath;
    private IReadOnlyList<PointCloudRenderVertex> _pointCloudVertices = Array.Empty<PointCloudRenderVertex>();
    private CancellationTokenSource? _scanCts;

    public bool Is2DMode
    {
        get
        {
            return _previewMode == PreviewMode.Height2D;
        }
        set
        {
            if (value)
            {
                SetPreviewMode(PreviewMode.Height2D);
            }
        }
    }

    public bool Is3DMode
    {
        get
        {
            return _previewMode == PreviewMode.PointCloud;
        }
        set
        {
            if (value)
            {
                SetPreviewMode(PreviewMode.PointCloud);
            }
        }
    }

    public bool IsGlbMode
    {
        get
        {
            return _previewMode == PreviewMode.Glb;
        }
        set
        {
            if (value)
            {
                SetPreviewMode(PreviewMode.Glb);
            }
        }
    }

    public CoordinateInfo MachineCoordinate { get
    {
        return _machineCoordinate;
    } set
    {
        SetProperty(ref _machineCoordinate, value);
    } }
    public CoordinateInfo WorkpieceCoordinate { get
    {
        return _workpieceCoordinate;
    } set
    {
        SetProperty(ref _workpieceCoordinate, value);
    } }
    public bool IsScanning
    {
        get
        {
            return _isScanning;
        }
        set
        {
            if (SetProperty(ref _isScanning, value))
                CommandManager.InvalidateRequerySuggested();
        }
    }
    public string ScanStatusText { get
    {
        return _scanStatusText;
    } set
    {
        SetProperty(ref _scanStatusText, value);
    } }
    public SystemStatus OverallStatus { get
    {
        return _overallStatus;
    } set
    {
        SetProperty(ref _overallStatus, value);
    } }
    public string LastPlyPath { get
    {
        return _lastPlyPath;
    } set
    {
        SetProperty(ref _lastPlyPath, value);
    } }
    public string? GlbPath { get
    {
        return _glbPath;
    } set
    {
        SetProperty(ref _glbPath, value);
    } }
    public IReadOnlyList<PointCloudRenderVertex> PointCloudVertices { get
    {
        return _pointCloudVertices;
    } set
    {
        SetProperty(ref _pointCloudVertices, value);
    } }

    public ICommand StartScanCommand { get; }
    public ICommand StopScanCommand { get; }
    public ICommand TestLoadPointCloudCommand { get; }

    public PrintingViewModel()
        : this(AppConfiguration.Load())
    {
    }

    public PrintingViewModel(IConfiguration config)
    {
        _config = config;
        StartScanCommand = new RelayCommand(_ => _ = StartAsync(), _ => !IsScanning);
        StopScanCommand = new RelayCommand(_ => _ = StopAsync(), _ => IsScanning);
        TestLoadPointCloudCommand = new RelayCommand(_ => _ = TestLoad3DFileAsync(), _ => !IsScanning);
    }

    private void SetPreviewMode(PreviewMode mode)
    {
        if (_previewMode == mode) return;
        _previewMode = mode;
        OnPropertyChanged(nameof(Is2DMode));
        OnPropertyChanged(nameof(Is3DMode));
        OnPropertyChanged(nameof(IsGlbMode));
    }

    /// <summary>測試用：載入 PLY 點雲或 Pipeline 產出的 GLB，方便比對視覺效果。</summary>
    private async Task TestLoad3DFileAsync()
    {
        var dlg = new OpenFileDialog
        {
            Title = "測試：載入 PLY 點雲或 GLB 網格",
            Filter = "3D 檔案|*.ply;*.glb|PLY 點雲 (*.ply)|*.ply|GLB 網格 (*.glb)|*.glb|所有檔案 (*.*)|*.*",
            CheckFileExists = true
        };
        if (dlg.ShowDialog() != true) return;

        var path = dlg.FileName;
        var ext = Path.GetExtension(path);

        if (ext.Equals(".glb", StringComparison.OrdinalIgnoreCase))
        {
            LastPlyPath = path;
            GlbPath = path;
            SetPreviewMode(PreviewMode.Glb);
            ScanStatusText = $"測試載入 GLB：{Path.GetFileName(path)}";
            OverallStatus = SystemStatus.Normal;
            return;
        }

        LastPlyPath = path;
        ScanStatusText = "測試載入（原始座標）…";
        OverallStatus = SystemStatus.Warning;
        SetPreviewMode(PreviewMode.PointCloud);

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

        _scanCts = new CancellationTokenSource();
        var ct = _scanCts.Token;

        ScanStatusText = "掃描中（面掃描／C# Pipeline）…";
        OverallStatus = SystemStatus.Warning;
        IsScanning = true;

        try
        {
            var scannerId = ResolvePrintingScannerId();
            var programNo = ResolveDefaultProgramNo(scannerId);

            using var scope = WpfServiceProviderFactory.CreateScope(_config);
            var captureFactory = scope.ServiceProvider.GetRequiredService<IScannerCaptureFactory>();
            var capture = captureFactory.GetCapture(scannerId);
            var result = await capture.CaptureWithAngleSteelAsync(programNo, ct);

            if (ct.IsCancellationRequested)
            {
                ScanStatusText = "已停止";
                OverallStatus = SystemStatus.Normal;
                return;
            }

            if (string.IsNullOrWhiteSpace(result.PlyPath) || !File.Exists(result.PlyPath))
            {
                ScanStatusText = "掃描完成但沒有角鋼 PLY";
                OverallStatus = SystemStatus.Error;
                return;
            }

            LastPlyPath = result.PlyPath;
            GlbPath = result.GlbPath;
            ScanStatusText = "掃描完成，載入點雲中…";
            OverallStatus = SystemStatus.Normal;
            Is3DMode = true;
            PointCloudVertices = await LoadPointCloudAsync(LastPlyPath, ct);

            var summary = PointCloudVertices.Count > 0
                ? $"掃描完成：{Path.GetFileName(LastPlyPath)}（顯示 {PointCloudVertices.Count:N0} 點）"
                : $"掃描完成但無可顯示點雲：{Path.GetFileName(LastPlyPath)}";
            if (result.AngleDegrees is { } ang)
            {
                summary += $" · 夾角 {ang:F2}°";
            }

            ScanStatusText = summary;
        }
        catch (OperationCanceledException)
        {
            ScanStatusText = "已停止";
            OverallStatus = SystemStatus.Normal;
        }
        catch (Exception ex)
        {
            ScanStatusText = "掃描異常：" + ex.GetBaseException().Message;
            OverallStatus = SystemStatus.Error;
        }
        finally
        {
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

    private string ResolvePrintingScannerId()
    {
        var section = _config.GetSection("Scanners");
        foreach (var child in section.GetChildren())
        {
            if (string.IsNullOrWhiteSpace(child.Key)) continue;
            var area = child["MachineArea"];
            if (string.Equals(area, "printing", StringComparison.OrdinalIgnoreCase))
            {
                return child.Key;
            }
        }

        throw new InvalidOperationException("appsettings 的 Scanners 沒有 MachineArea=printing 的掃描器。");
    }

    private byte ResolveDefaultProgramNo(string scannerId)
    {
        var navScan = _config.GetValue<int?>($"Scanners:{scannerId}:NavigatorScanNo") ?? 1;
        return (byte)Math.Clamp(navScan - 1, 0, 15);
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
}
