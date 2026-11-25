using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using ThreeDScan.Common;
using ThreeDScan.Domain.Abstractions;
using ThreeDScan.Domain.Models;
using ThreeDScan.Models;
using ThreeDScan.Services;

namespace ThreeDScan.ViewModels;

// 噴印／割切共用：API GLB 監看、2D／點雲／GLB 預覽與右側前緣座標。
public class SurfaceScanAreaViewModel : ViewModelBase
{
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
    private SystemStatus _connectionLamp = SystemStatus.Error;
    private string _connectionStatusText = "正在檢查連線…";
    private bool _connectionPollBusy;
    private DispatcherTimer? _connectionTimer;
    private string _lastPlyPath = "";
    private string? _glbPath;
    private IReadOnlyList<PointCloudRenderVertex> _pointCloudVertices = Array.Empty<PointCloudRenderVertex>();
    private ImageSource? _preview2dTop;
    private ImageSource? _preview2dFront;
    private ImageSource? _preview2dSide;
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

    /// <summary>連線狀態燈（Normal＝綠，Error＝紅）。</summary>
    public SystemStatus ConnectionLamp
    {
        get
        {
            return _connectionLamp;
        }
        set
        {
            SetProperty(ref _connectionLamp, value);
        }
    }

    /// <summary>連線狀態說明（掃描服務與該區面掃描器）。</summary>
    public string ConnectionStatusText
    {
        get
        {
            return _connectionStatusText;
        }
        set
        {
            SetProperty(ref _connectionStatusText, value);
        }
    }

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

    public SurfaceScanAreaViewModel(IConfiguration config, string machineArea)
    {
        _config = config;
        _machineArea = string.IsNullOrWhiteSpace(machineArea) ? "printing" : machineArea.Trim();
        try
        {
            StartWatchingApiGlb();
        }
        catch (Exception)
        {
        }

        StartConnectionMonitoring();
    }

    private void StartConnectionMonitoring()
    {
        _connectionTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(3),
        };
        _connectionTimer.Tick += OnConnectionPollTick;
        _connectionTimer.Start();
        _ = RefreshConnectionAsync();
    }

    private void OnConnectionPollTick(object? sender, EventArgs e)
    {
        _ = RefreshConnectionAsync();
    }

    private async Task RefreshConnectionAsync()
    {
        if (_connectionPollBusy)
        {
            return;
        }

        _connectionPollBusy = true;
        try
        {
            var deviceOk = await ProbeSurfaceScannerAsync().ConfigureAwait(false);
            var suffix = IsScanning ? "面掃描進行中" : "";
            string deviceLabel;
            if (MachineAreaScannerResolver.TryResolve(_config, _machineArea, out var scannerId))
            {
                deviceLabel = "面掃描器 " + scannerId;
            }
            else
            {
                deviceLabel = "面掃描器";
                deviceOk = false;
            }

            var display = AreaConnectionDisplay.Build(deviceOk, deviceLabel, suffix);
            await RunOnUiAsync(() =>
            {
                ConnectionLamp = display.Lamp;
                ConnectionStatusText = display.Text;
            });
        }
        finally
        {
            _connectionPollBusy = false;
        }
    }

    private async Task<bool> ProbeSurfaceScannerAsync()
    {
        if (!MachineAreaScannerResolver.TryResolve(_config, _machineArea, out var scannerId))
        {
            return false;
        }

        var registry = WpfServiceProviderFactory.GetScannerRegistry(_config);
        if (!registry.TryGetEndpoint(scannerId, out var endpoint))
        {
            return false;
        }

        return await TcpEndpointConnectivity.TryConnectAsync(endpoint.Ip, endpoint.Port, 2500).ConfigureAwait(false);
    }

    private static async Task RunOnUiAsync(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess())
        {
            action();
            return;
        }

        await dispatcher.InvokeAsync(action);
    }

    private void SetPreviewMode(PreviewMode mode)
    {
        if (_previewMode == mode) return;
        _previewMode = mode;
        OnPropertyChanged(nameof(Is2DMode));
        OnPropertyChanged(nameof(Is3DMode));
        OnPropertyChanged(nameof(IsGlbMode));
    }


    /// <summary>載入 ASCII PLY 為顯示用點雲（實作見 <see cref="AsciiPlyPointCloudDisplayLoader"/>，研磨區共用）。</summary>
    private static Task<IReadOnlyList<PointCloudRenderVertex>> LoadDisplayPointCloudAsync(
        string plyPath,
        CancellationToken token)
    {
        return AsciiPlyPointCloudDisplayLoader.LoadAsync(plyPath, token);
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

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
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

    /// <summary>同目錄的 PLY 與拼接產出的 top／front／side.png 一併載入，切換模式即可看。</summary>
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

        Preview2DTop = TryLoadBitmap(Path.Combine(dir, "top.png"));
        Preview2DFront = TryLoadBitmap(Path.Combine(dir, "front.png"));
        Preview2DSide = TryLoadBitmap(Path.Combine(dir, "side.png"));

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

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
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
