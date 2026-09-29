using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Input;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using ThreeDScan.Common;
using ThreeDScan.Domain.Abstractions;
using ThreeDScan.Domain.Models;
using ThreeDScan.Models;

namespace ThreeDScan.ViewModels;

// 噴印／割切共用：API GLB 監看、2D／點雲／GLB 預覽與右側前緣座標。
public class SurfaceScanAreaViewModel : ViewModelBase
{
    /// <summary>WPF 端效能上限；點數超過時依檔案順序等距跳點。</summary>
    private const int MaxDisplayPoints = 60_000;

    private readonly IConfiguration _config;
    private readonly string _machineArea;

    private enum PreviewMode
    {
        Height2D,
        PointCloud,
        Glb,
    }

    private PreviewMode _previewMode = PreviewMode.PointCloud;
    private HeadFeatureDisplayInfo _headFeatures = new();
    private bool _isScanning;
    private string _scanStatusText = "就緒 — 等待面掃描";
    private SystemStatus _overallStatus = SystemStatus.Normal;
    private string _lastPlyPath = "";
    private string? _glbPath;
    private IReadOnlyList<PointCloudRenderVertex> _pointCloudVertices = Array.Empty<PointCloudRenderVertex>();
    private ImageSource? _preview2dTop;
    private ImageSource? _preview2dFront;
    private ImageSource? _preview2dSide;
    // private CancellationTokenSource? _scanCts;
    private FileSystemWatcher? _glbWatch;
    private int _glbWatchVersion;

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

    public HeadFeatureDisplayInfo HeadFeatures
    {
        get
        {
            return _headFeatures;
        }
        set
        {
            SetProperty(ref _headFeatures, value);
        }
    }

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
    public IReadOnlyList<PointCloudRenderVertex> PointCloudVertices
    {
        get
        {
            return _pointCloudVertices;
        }
        set
        {
            SetProperty(ref _pointCloudVertices, value);
        }
    }

    public ImageSource? Preview2DTop
    {
        get
        {
            return _preview2dTop;
        }
        set
        {
            if (SetProperty(ref _preview2dTop, value))
            {
                OnPropertyChanged(nameof(Has2DPreview));
            }
        }
    }

    public ImageSource? Preview2DFront
    {
        get
        {
            return _preview2dFront;
        }
        set
        {
            if (SetProperty(ref _preview2dFront, value))
            {
                OnPropertyChanged(nameof(Has2DPreview));
            }
        }
    }

    public ImageSource? Preview2DSide
    {
        get
        {
            return _preview2dSide;
        }
        set
        {
            if (SetProperty(ref _preview2dSide, value))
            {
                OnPropertyChanged(nameof(Has2DPreview));
            }
        }
    }

    public bool Has2DPreview
    {
        get
        {
            return _preview2dTop is not null
                || _preview2dFront is not null
                || _preview2dSide is not null;
        }
    }

    // 本機開始／停止掃描僅測試用；正式走 API。
    // public ICommand StartScanCommand { get; }
    // public ICommand StopScanCommand { get; }

    public SurfaceScanAreaViewModel(IConfiguration config, string machineArea)
    {
        _config = config;
        _machineArea = string.IsNullOrWhiteSpace(machineArea) ? "printing" : machineArea.Trim();
        // StartScanCommand = new RelayCommand(_ => _ = StartAsync(), _ => !IsScanning);
        // StopScanCommand = new RelayCommand(_ => _ = StopAsync(), _ => IsScanning);
        try
        {
            StartWatchingApiGlb();
        }
        catch (Exception)
        {
        }
    }

    private void SetPreviewMode(PreviewMode mode)
    {
        if (_previewMode == mode) return;
        _previewMode = mode;
        OnPropertyChanged(nameof(Is2DMode));
        OnPropertyChanged(nameof(Is3DMode));
        OnPropertyChanged(nameof(IsGlbMode));
    }

    // 本機擷取僅測試用；正式走 API surface / surface-from-file。
#if false
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
            var scannerId = ResolveAreaScannerId();
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
            OverallStatus = SystemStatus.Normal;
            if (!string.IsNullOrWhiteSpace(result.GlbPath) && File.Exists(result.GlbPath))
            {
                SetPreviewMode(PreviewMode.Glb);
                GlbPath = result.GlbPath;
                ScanStatusText = "掃描完成，載入 GLB…";
            }
            else
            {
                Is3DMode = true;
                ScanStatusText = "掃描完成，載入點雲中…";
            }

            PointCloudVertices = await LoadPointCloudAsync(LastPlyPath, ct);

            var summary = !string.IsNullOrWhiteSpace(result.GlbPath) && File.Exists(result.GlbPath)
                ? $"掃描完成：{Path.GetFileName(result.GlbPath)}（前緣三點標註）"
                : PointCloudVertices.Count > 0
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

    private string ResolveAreaScannerId()
    {
        var section = _config.GetSection("Scanners");
        foreach (var child in section.GetChildren())
        {
            if (string.IsNullOrWhiteSpace(child.Key)) continue;
            var area = child["MachineArea"];
            if (string.Equals(area, _machineArea, StringComparison.OrdinalIgnoreCase))
            {
                return child.Key;
            }
        }

        throw new InvalidOperationException(
            "appsettings 的 Scanners 沒有 MachineArea=" + _machineArea + " 的掃描器。");
    }

    private byte ResolveDefaultProgramNo(string scannerId)
    {
        var navScan = _config.GetValue<int?>($"Scanners:{scannerId}:NavigatorScanNo") ?? 1;
        return (byte)Math.Clamp(navScan - 1, 0, 15);
    }

    /// <summary>
    /// 座標 flatten／voxel／抽樣後著色；依 X/Y/Z 中範圍最大的軸上色。
    /// </summary>
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
#endif

    /// <summary>
    /// 顯示用點雲：不 flatten／不 voxel；超過上限時等距跳點。
    /// </summary>
    private static async Task<IReadOnlyList<PointCloudRenderVertex>> LoadDisplayPointCloudAsync(
        string plyPath,
        CancellationToken token)
    {
        return await Task.Run(() =>
        {
            if (!File.Exists(plyPath)
                || !TryParseAsciiPlyPoints(plyPath, token, out var raw)
                || raw.Count == 0)
            {
                return (IReadOnlyList<PointCloudRenderVertex>)Array.Empty<PointCloudRenderVertex>();
            }

            int n = raw.Count;
            int step = 1;
            if (MaxDisplayPoints > 0 && n > MaxDisplayPoints)
            {
                step = (n + MaxDisplayPoints - 1) / MaxDisplayPoints;
            }

            var picked = new List<(double X, double Y, double Z)>(
                Math.Min(n, MaxDisplayPoints > 0 ? MaxDisplayPoints : n));
            for (int i = 0; i < n; i += step)
            {
                token.ThrowIfCancellationRequested();
                picked.Add(raw[i]);
                if (MaxDisplayPoints > 0 && picked.Count >= MaxDisplayPoints)
                {
                    break;
                }
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
            if (line.Length == 0)
            {
                continue;
            }

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

            if (vertexCount > 0 && dataIndex >= vertexCount)
            {
                break;
            }

            dataIndex++;

            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3)
            {
                continue;
            }

            if (double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
                && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y)
                && double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var z))
            {
                points.Add((x, y, z));
            }
        }

        return points.Count > 0;
    }

    private static void ComputeBounds(
        IReadOnlyList<(double X, double Y, double Z)> pts,
        out double minX, out double maxX, out double minY, out double maxY, out double minZ, out double maxZ)
    {
        minX = minY = minZ = double.MaxValue;
        maxX = maxY = maxZ = double.MinValue;
        foreach (var p in pts)
        {
            if (p.X < minX)
            {
                minX = p.X;
            }

            if (p.X > maxX)
            {
                maxX = p.X;
            }

            if (p.Y < minY)
            {
                minY = p.Y;
            }

            if (p.Y > maxY)
            {
                maxY = p.Y;
            }

            if (p.Z < minZ)
            {
                minZ = p.Z;
            }

            if (p.Z > maxZ)
            {
                maxZ = p.Z;
            }
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

    /// <summary>監看 API 寫入 ContentsRoot 的最新 GLB 指標；啟動時不載入既有結果。</summary>
    private void StartWatchingApiGlb()
    {
        var pointer = ScanMonitorPaths.GlbPointerFile(_config["Storage:ContentsRoot"], _machineArea);
        if (string.IsNullOrWhiteSpace(pointer))
        {
            return;
        }

        var dir = Path.GetDirectoryName(pointer);
        if (string.IsNullOrWhiteSpace(dir))
        {
            return;
        }

        Directory.CreateDirectory(dir);
        _glbWatch = new FileSystemWatcher(dir, ScanMonitorPaths.PointerFileNameForArea(_machineArea))
        {
            NotifyFilter = NotifyFilters.LastWrite
                | NotifyFilters.FileName
                | NotifyFilters.CreationTime
                | NotifyFilters.Size,
            EnableRaisingEvents = true,
        };
        _glbWatch.Created += (_, _) => QueueLoadApiGlb();
        _glbWatch.Changed += (_, _) => QueueLoadApiGlb();
    }

    private void QueueLoadApiGlb()
    {
        var version = Interlocked.Increment(ref _glbWatchVersion);
        _ = LoadApiGlbAfterDelayAsync(version);
    }

    private async Task LoadApiGlbAfterDelayAsync(int version)
    {
        try
        {
            await Task.Delay(400).ConfigureAwait(false);
        }
        catch (TaskCanceledException)
        {
            return;
        }

        if (version != Volatile.Read(ref _glbWatchVersion))
        {
            return;
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            LoadApiGlbPointer();
            return;
        }

        await dispatcher.InvokeAsync(LoadApiGlbPointer);
    }

    private void LoadApiGlbPointer()
    {
        var pointer = ScanMonitorPaths.GlbPointerFile(_config["Storage:ContentsRoot"], _machineArea);
        if (string.IsNullOrWhiteSpace(pointer) || !File.Exists(pointer))
        {
            return;
        }

        string glbPath;
        try
        {
            glbPath = File.ReadAllText(pointer).Trim();
        }
        catch (IOException)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(glbPath) || !File.Exists(glbPath))
        {
            return;
        }

        SetPreviewMode(PreviewMode.Glb);
        if (string.Equals(GlbPath, glbPath, StringComparison.OrdinalIgnoreCase))
        {
            GlbPath = null;
        }

        LastPlyPath = glbPath;
        GlbPath = glbPath;
        HeadFeatures.LoadFromGlb(glbPath);
        LoadSiblingPreviews(glbPath, _glbWatchVersion);
        ScanStatusText = $"API 結果：{Path.GetFileName(glbPath)}";
        OverallStatus = SystemStatus.Normal;
    }

    /// <summary>同目錄的 PLY 與三張 2D PNG 一併載入，切換模式即可看。</summary>
    private void LoadSiblingPreviews(string glbPath, int version)
    {
        var dir = Path.GetDirectoryName(glbPath);
        var stem = Path.GetFileNameWithoutExtension(glbPath);
        if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(stem))
        {
            Preview2DTop = null;
            Preview2DFront = null;
            Preview2DSide = null;
            PointCloudVertices = Array.Empty<PointCloudRenderVertex>();
            return;
        }

        Preview2DTop = TryLoadBitmap(Path.Combine(dir, stem + "_2d_top.png"));
        Preview2DFront = TryLoadBitmap(Path.Combine(dir, stem + "_2d_front.png"));
        Preview2DSide = TryLoadBitmap(Path.Combine(dir, stem + "_2d_side.png"));

        var plyPath = Path.Combine(dir, stem + ".ply");
        if (!File.Exists(plyPath))
        {
            PointCloudVertices = Array.Empty<PointCloudRenderVertex>();
            return;
        }

        _ = LoadPointCloudThenApplyAsync(plyPath, version);
    }

    private async Task LoadPointCloudThenApplyAsync(string plyPath, int version)
    {
        IReadOnlyList<PointCloudRenderVertex> verts;
        try
        {
            verts = await LoadDisplayPointCloudAsync(plyPath, CancellationToken.None);
        }
        catch (Exception)
        {
            verts = Array.Empty<PointCloudRenderVertex>();
        }

        if (version != Volatile.Read(ref _glbWatchVersion))
        {
            return;
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            PointCloudVertices = verts;
            return;
        }

        await dispatcher.InvokeAsync(() =>
        {
            if (version != Volatile.Read(ref _glbWatchVersion))
            {
                return;
            }

            PointCloudVertices = verts;
        });
    }

    private static ImageSource? TryLoadBitmap(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = stream;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
