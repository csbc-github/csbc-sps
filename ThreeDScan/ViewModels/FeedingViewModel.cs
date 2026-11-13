using System.Collections.ObjectModel;
using Microsoft.Extensions.Configuration;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using LMX;
using ThreeDScan.Common;
using ThreeDScan.Domain.Models;
using ThreeDScan.Models;
using ThreeDScan.Services;

namespace ThreeDScan.ViewModels;

// 進料區 VM：對應 SA UC-03 線掃描（角鋼三特徵點）／ SD §10.2
public class FeedingViewModel : ViewModelBase
{
    private double _totalArea;
    private int _contourCount;
    private string _scanStatusText = "就緒 — 等待開始掃描";
    private SystemStatus _overallStatus = SystemStatus.Normal;
    private SystemStatus _lineOverallStatus = SystemStatus.Warning;
    private string _lineResultHeadline = "等待結果";
    private string _lineResultStatusText = "等待線掃描完成";
    private string _lineResultKey = "";
    private bool _linePollBusy;
    private DispatcherTimer? _linePollTimer;

    private static readonly HttpClient LineResultHttp = new(new HttpClientHandler
    {
        ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
    })
    {
        Timeout = TimeSpan.FromSeconds(30),
    };

    private LidarFrame? _currentFrame;
    private bool _isScanning;
    private bool _userStoppedLive;
    private bool _livePollBusy;
    private string _liveHint = "正在連接線掃描器…";
    private DispatcherTimer? _liveTimer;
    private bool _selectingRegion;
    private LineScanRegion? _sensorRegion;
    private SystemStatus _connectionLamp = SystemStatus.Error;
    private string _connectionStatusText = "正在檢查連線…";
    private bool _connectionPollBusy;
    private DispatcherTimer? _connectionTimer;
    private bool _apiLineRecording;
    private bool _profileFrozen;
    private bool _isPickingDesktopPoints;
    private int _desktopPointsChosen;

    // 剖面圖校正（綁定到 LidarScanView；之後可改為設定檔）
    private string _lidarCalibAngleMinDeg = "";
    private string _lidarCalibAngleMaxDeg = "";
    private string _lidarCalibAngleCorrectionDeg = "0";
    private string _lidarCalibXOffsetMm = "0";
    private string _lidarCalibZOffsetMm = "0";
    private string _lidarCalibReferenceYMm = "";

    public ObservableCollection<MaterialItem> Materials { get; } = new();

    /// <summary>線掃描 complete 回傳的各支角鋼，顯示在進料區右側。</summary>
    public ObservableCollection<MaterialItem> LineResults { get; } = new();

    public double TotalArea { get
    {
        return _totalArea;
    } set
    {
        SetProperty(ref _totalArea, value);
    } }
    public int ContourCount { get
    {
        return _contourCount;
    } set
    {
        SetProperty(ref _contourCount, value);
    } }
    public string ScanStatusText { get
    {
        return _scanStatusText;
    } set
    {
        SetProperty(ref _scanStatusText, value);
    } }

    // 進料區整體狀態燈（綠/黃/紅），對應 SA NFR-U-01
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

    /// <summary>連線狀態說明（掃描服務與線掃光達）。</summary>
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

    /// <summary>右側結果區的狀態燈。</summary>
    public SystemStatus LineOverallStatus
    {
        get
        {
            return _lineOverallStatus;
        }
        set
        {
            SetProperty(ref _lineOverallStatus, value);
        }
    }

    /// <summary>右側標題旁的結果摘要。</summary>
    public string LineResultHeadline
    {
        get
        {
            return _lineResultHeadline;
        }
        set
        {
            SetProperty(ref _lineResultHeadline, value);
        }
    }

    /// <summary>右側底部說明最近一次 complete 的狀態。</summary>
    public string LineResultStatusText
    {
        get
        {
            return _lineResultStatusText;
        }
        set
        {
            SetProperty(ref _lineResultStatusText, value);
        }
    }

    /// <summary>左側還沒有截面時的說明。</summary>
    public string LiveHint
    {
        get
        {
            return _liveHint;
        }
        set
        {
            SetProperty(ref _liveHint, value);
        }
    }

    public LidarFrame? CurrentFrame { get
    {
        return _currentFrame;
    } set
    {
        SetProperty(ref _currentFrame, value);
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

    public ICommand StartScanCommand { get; }
    public ICommand StopScanCommand { get; }
    public ICommand BeginSelectRegionCommand { get; }
    public ICommand ClearRegionCommand { get; }
    public ICommand SetDesktopCalibrationCommand { get; }
    public ICommand ClearDesktopCalibrationCommand { get; }

    /// <summary>API 原生 double XY 輪廓；即時繪圖與凍結點選共用同一批座標。</summary>
    public ObservableCollection<ScannerPlanePoint> ProfileSnapPoints { get; } = new();

    /// <summary>輪廓已凍結時不再覆寫即時剖面。</summary>
    public bool IsProfileFrozen
    {
        get
        {
            return _profileFrozen;
        }
        set
        {
            if (SetProperty(ref _profileFrozen, value))
            {
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    /// <summary>正在點選桌面校正兩點。</summary>
    public bool IsPickingDesktopPoints
    {
        get
        {
            return _isPickingDesktopPoints;
        }
        set
        {
            SetProperty(ref _isPickingDesktopPoints, value);
        }
    }

    private double? _desktopFirstX;
    private double? _desktopFirstY;
    private double? _desktopSecondX;
    private double? _desktopSecondY;

    public double? DesktopFirstX
    {
        get
        {
            return _desktopFirstX;
        }
        set
        {
            if (SetProperty(ref _desktopFirstX, value))
            {
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public double? DesktopFirstY
    {
        get
        {
            return _desktopFirstY;
        }
        set
        {
            if (SetProperty(ref _desktopFirstY, value))
            {
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public double? DesktopSecondX
    {
        get
        {
            return _desktopSecondX;
        }
        set
        {
            if (SetProperty(ref _desktopSecondX, value))
            {
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public double? DesktopSecondY
    {
        get
        {
            return _desktopSecondY;
        }
        set
        {
            if (SetProperty(ref _desktopSecondY, value))
            {
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    /// <summary>正在剖面上拖曳框選時為 true，此時畫面停在目前截面。</summary>
    public bool IsSelectingRegion
    {
        get
        {
            return _selectingRegion;
        }
        set
        {
            SetProperty(ref _selectingRegion, value);
        }
    }

    /// <summary>已套用的掃描器平面框。後續 C# 量測只保留框內的點。</summary>
    public LineScanRegion? SensorRegion
    {
        get
        {
            return _sensorRegion;
        }
        set
        {
            SetProperty(ref _sensorRegion, value);
        }
    }

    /// <summary>角度下限（°），空白＝不限制。例：只取朝下方時可填 70。</summary>
    public string LidarCalibAngleMinDeg { get
    {
        return _lidarCalibAngleMinDeg;
    } set
    {
        SetProperty(ref _lidarCalibAngleMinDeg, value);
    } }

    /// <summary>角度上限（°），空白＝不限制。例：110。</summary>
    public string LidarCalibAngleMaxDeg { get
    {
        return _lidarCalibAngleMaxDeg;
    } set
    {
        SetProperty(ref _lidarCalibAngleMaxDeg, value);
    } }

    /// <summary>角度平移（°），加在公式 θ = i·解析度 − 起始角 之後。</summary>
    public string LidarCalibAngleCorrectionDeg { get
    {
        return _lidarCalibAngleCorrectionDeg;
    } set
    {
        SetProperty(ref _lidarCalibAngleCorrectionDeg, value);
    } }

    /// <summary>X 平移（mm），畫面與物料區 X 一併平移。</summary>
    public string LidarCalibXOffsetMm { get
    {
        return _lidarCalibXOffsetMm;
    } set
    {
        SetProperty(ref _lidarCalibXOffsetMm, value);
    } }

    /// <summary>Z 平移（mm），整條剖面上下平移。</summary>
    public string LidarCalibZOffsetMm { get
    {
        return _lidarCalibZOffsetMm;
    } set
    {
        SetProperty(ref _lidarCalibZOffsetMm, value);
    } }

    /// <summary>參考面 Y（mm，雷達座標），空白＝自動用有效點 Y 的中位數。</summary>
    public string LidarCalibReferenceYMm { get
    {
        return _lidarCalibReferenceYMm;
    } set
    {
        SetProperty(ref _lidarCalibReferenceYMm, value);
    } }

    public FeedingViewModel()
    {
        StartScanCommand = new RelayCommand(_ => _ = StartAsync(), _ => !IsScanning);
        StopScanCommand = new RelayCommand(_ => _ = StopAsync(), _ => IsScanning);
        BeginSelectRegionCommand = new RelayCommand(_ => _ = BeginSelectRegionAsync());
        ClearRegionCommand = new RelayCommand(_ => _ = ClearRegionAsync());
        SetDesktopCalibrationCommand = new RelayCommand(
            _ => _ = SetDesktopCalibrationAsync(),
            _ => !IsPickingDesktopPoints && !IsSelectingRegion);
        ClearDesktopCalibrationCommand = new RelayCommand(
            _ => _ = ClearDesktopCalibrationAsync(),
            _ => !IsPickingDesktopPoints);

        LoadDesignTimeData();
        if (!IsInDesignMode())
        {
            LineResults.Clear();
            StartLineResultPolling();
            StartLiveWatch();
            StartConnectionMonitoring();
            _ = LoadScannerSettingsAsync();
        }
    }

    /// <summary>視窗關閉時停止輪詢並結束架設預覽。</summary>
    public async Task CleanupAsync()
    {
        _liveTimer?.Stop();
        _connectionTimer?.Stop();
        _linePollTimer?.Stop();
        if (IsScanning)
        {
            await StopAsync().ConfigureAwait(false);
        }
    }

    /// <summary>剖面點選回呼（由 FeedingView 轉發）。</summary>
    public void ApplyDesktopPointPick(ScannerPlanePoint point)
    {
        if (!IsPickingDesktopPoints)
        {
            return;
        }

        if (_desktopPointsChosen == 0)
        {
            DesktopFirstX = point.X;
            DesktopFirstY = point.Y;
            _desktopPointsChosen = 1;
            ScanStatusText = "已選第一點，請點第二點";
            return;
        }

        DesktopSecondX = point.X;
        DesktopSecondY = point.Y;
        _desktopPointsChosen = 2;
        IsPickingDesktopPoints = false;
        ScanStatusText = "兩點已選定，正在儲存…";
        _ = SubmitTwoPointAndResumeAsync();
    }

    private async Task SubmitTwoPointAndResumeAsync()
    {
        var saved = await SubmitTwoPointAsync().ConfigureAwait(false);
        if (saved)
        {
            await ResumeLiveAfterProfileAsync().ConfigureAwait(false);
        }
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
            var config = AppConfiguration.Load();
            var deviceOk = await ProbeLineScannerAsync(config).ConfigureAwait(false);
            var suffix = _apiLineRecording ? "線掃描錄製中" : "";
            var deviceLabel = ResolveLineDeviceLabel(config);
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

    private static string ResolveLineDeviceLabel(IConfiguration config)
    {
        if (WpfServiceProviderFactory.TryResolveLineScannerEndpoint(config, out var endpoint))
        {
            return "線掃光達 " + endpoint.ScannerId;
        }

        return "線掃光達";
    }

    private async Task<bool> ProbeLineScannerAsync(IConfiguration config)
    {
        if (!WpfServiceProviderFactory.TryResolveLineScannerEndpoint(config, out var endpoint))
        {
            return false;
        }

        return await TcpEndpointConnectivity.TryConnectAsync(endpoint.Ip, endpoint.Port, 2500).ConfigureAwait(false);
    }

    private async Task BeginSelectRegionAsync()
    {
        if (IsSelectingRegion)
        {
            IsSelectingRegion = false;
            ScanStatusText = "已取消框選";
            await ResumeLiveAfterProfileAsync().ConfigureAwait(false);
            return;
        }

        CancelDesktopCalibration();
        ScanStatusText = "正在凍結輪廓…";
        await CaptureProfileAsync().ConfigureAwait(false);
        if (!IsProfileFrozen || ProfileSnapPoints.Count < 2)
        {
            ScanStatusText = "還沒有剖面，無法框選";
            await ResumeLiveAfterProfileAsync().ConfigureAwait(false);
            return;
        }

        IsSelectingRegion = true;
        ScanStatusText = "在左側剖面上拖曳框選範圍";
    }

    private async Task SetDesktopCalibrationAsync()
    {
        CancelDesktopCalibration();
        IsSelectingRegion = false;
        await CaptureProfileAsync().ConfigureAwait(false);
        if (!IsProfileFrozen || ProfileSnapPoints.Count == 0)
        {
            return;
        }

        _desktopPointsChosen = 0;
        DesktopFirstX = null;
        DesktopFirstY = null;
        DesktopSecondX = null;
        DesktopSecondY = null;
        IsPickingDesktopPoints = true;
        ScanStatusText = "在凍結輪廓上點選第一點（桌面校正）";
    }

    private void CancelDesktopCalibration()
    {
        IsPickingDesktopPoints = false;
        _desktopPointsChosen = 0;
    }

    private async Task ResumeLiveAfterProfileAsync()
    {
        IsProfileFrozen = false;
        ProfileSnapPoints.Clear();
        IsPickingDesktopPoints = false;
        _desktopPointsChosen = 0;
        IsSelectingRegion = false;
        LiveHint = "架設預覽";
        ScanStatusText = "已恢復即時剖面";
        if (!_userStoppedLive)
        {
            await StartAsync().ConfigureAwait(false);
        }
    }

    /// <summary>套用畫面上拖出的框，並寫入掃描服務。</summary>
    public void ApplyChosenRegion(LineScanRegion? region)
    {
        if (region == null)
        {
            ScanStatusText = "框太小或超出圖面，請再拖一次";
            return;
        }

        IsSelectingRegion = false;
        SensorRegion = region;
        ScanStatusText = "已設定掃描區域";
        _ = SaveRegionAndResumeAsync(region);
    }

    private async Task ClearRegionAsync()
    {
        IsSelectingRegion = false;
        SensorRegion = null;
        ScanStatusText = "已清除掃描區域";
        await SaveRegionAndResumeAsync(null).ConfigureAwait(false);
    }

    private static readonly JsonSerializerOptions RegionJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private static string ApiBase()
    {
        var baseUrl = AppConfiguration.Load()["CalibrationMonitor:ApiBaseUrl"];
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            baseUrl = "http://localhost:5295";
        }

        return baseUrl.TrimEnd('/');
    }

    private async Task LoadScannerSettingsAsync()
    {
        try
        {
            var scannerId = ResolveDefaultScannerId();
            var response = await LineResultHttp.GetAsync(ApiBase() + "/api/scanners/" + scannerId + "/settings")
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return;
            }

            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(json))
            {
                return;
            }

            await RunOnUiAsync(() => ApplyScannerSettingsJson(json));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
        }
    }

    private void ApplyScannerSettingsJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.TryGetProperty("roiXY", out var roiNode)
            && roiNode.ValueKind == JsonValueKind.Array
            && roiNode.GetArrayLength() == 4)
        {
            var bounds = new double[4];
            for (var i = 0; i < 4; i++)
            {
                bounds[i] = roiNode[i].GetDouble();
            }

            var region = new LineScanRegion
            {
                XMin = bounds[0],
                XMax = bounds[1],
                YMin = bounds[2],
                YMax = bounds[3],
            };
            if (region.XMax > region.XMin && region.YMax > region.YMin)
            {
                SensorRegion = region;
            }
        }

        if (root.TryGetProperty("calibration", out var cal) && cal.ValueKind == JsonValueKind.Object)
        {
            if (cal.TryGetProperty("first", out var first) && cal.TryGetProperty("second", out var second))
            {
                DesktopFirstX = ReadDouble(first, "x");
                DesktopFirstY = ReadDouble(first, "y");
                DesktopSecondX = ReadDouble(second, "x");
                DesktopSecondY = ReadDouble(second, "y");
            }
        }
        else
        {
            DesktopFirstX = null;
            DesktopFirstY = null;
            DesktopSecondX = null;
            DesktopSecondY = null;
        }
    }

    private async Task CaptureProfileAsync()
    {
        await RunOnUiAsync(() => IsProfileFrozen = false);
        try
        {
            var scannerId = ResolveDefaultScannerId();
            var body = JsonSerializer.Serialize(new { scannerId });
            var response = await LineResultHttp.PostAsync(
                ApiBase() + "/api/scan/profile",
                new StringContent(body, Encoding.UTF8, "application/json")).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                await RunOnUiAsync(() => ScanStatusText = "擷取輪廓失敗（HTTP " + (int)response.StatusCode + "）");
                return;
            }

            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            await RunOnUiAsync(() => ApplyCapturedProfile(json));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            await RunOnUiAsync(() => ScanStatusText = "擷取輪廓失敗：" + ex.Message);
        }
    }

    private void ApplyCapturedProfile(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (!root.TryGetProperty("points", out var points) || points.ValueKind != JsonValueKind.Array)
        {
            ScanStatusText = "回應無輪廓點";
            return;
        }

        var native = new List<ScannerPlanePoint>();
        foreach (var node in points.EnumerateArray())
        {
            var x = ReadDouble(node, "x");
            var y = ReadDouble(node, "y");
            native.Add(new ScannerPlanePoint { X = x, Y = y });
        }
        ApplyNativePoints(native);
        if (ProfileSnapPoints.Count < 2)
        {
            ScanStatusText = "有效輪廓點不足";
            return;
        }

        IsProfileFrozen = true;
        CancelDesktopCalibration();
        LiveHint = "輪廓已凍結";
        ScanStatusText = "請在剖面上點選兩點";
    }

    private async Task<bool> SubmitTwoPointAsync()
    {
        if (!DesktopFirstX.HasValue || !DesktopFirstY.HasValue || !DesktopSecondX.HasValue || !DesktopSecondY.HasValue)
        {
            return false;
        }

        try
        {
            var scannerId = ResolveDefaultScannerId();
            var payload = new
            {
                first = new { x = DesktopFirstX.Value, y = DesktopFirstY.Value },
                second = new { x = DesktopSecondX.Value, y = DesktopSecondY.Value },
            };
            var json = JsonSerializer.Serialize(payload);
            var response = await LineResultHttp.PostAsync(
                ApiBase() + "/api/scanners/" + scannerId + "/calibration/two-point",
                new StringContent(json, Encoding.UTF8, "application/json")).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                await RunOnUiAsync(() => ScanStatusText = "儲存兩點失敗：" + err);
                return false;
            }

            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            await RunOnUiAsync(() =>
            {
                ApplyScannerSettingsJson(body);
                ScanStatusText = "兩點桌面校正已儲存";
            });
            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            await RunOnUiAsync(() => ScanStatusText = "儲存兩點失敗：" + ex.Message);
            return false;
        }
    }

    private async Task ClearDesktopCalibrationAsync()
    {
        try
        {
            var scannerId = ResolveDefaultScannerId();
            var response = await LineResultHttp.DeleteAsync(
                ApiBase() + "/api/scanners/" + scannerId + "/calibration/two-point").ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                await RunOnUiAsync(() => ScanStatusText = "清除兩點失敗：" + err);
                return;
            }

            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            await RunOnUiAsync(() => ApplyScannerSettingsJson(body));
            await ResumeLiveAfterProfileAsync().ConfigureAwait(false);
            await RunOnUiAsync(() => ScanStatusText = "兩點桌面校正已清除");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            await RunOnUiAsync(() => ScanStatusText = "清除兩點失敗：" + ex.Message);
        }
    }

    private void ApplyNativePoints(IEnumerable<ScannerPlanePoint> points)
    {
        // Keep one exact scanner-XY point per API sample; no integer range or angle bins.
        var native = points.Where(p => double.IsFinite(p.X) && double.IsFinite(p.Y)
            && (p.X != 0 || p.Y != 0)).ToList();
        CurrentFrame = null;
        ProfileSnapPoints.Clear();
        foreach (var point in native) ProfileSnapPoints.Add(point);
    }

    private bool ApplyNativeLive(JsonElement xs, JsonElement ys)
    {
        if (xs.ValueKind != JsonValueKind.Array || ys.ValueKind != JsonValueKind.Array) return false;
        int count = Math.Min(xs.GetArrayLength(), ys.GetArrayLength());
        var points = new List<ScannerPlanePoint>(count);
        for (int i = 0; i < count; i++)
        {
            if (xs[i].TryGetDouble(out double x) && ys[i].TryGetDouble(out double y))
                points.Add(new ScannerPlanePoint { X = x, Y = y });
        }
        ApplyNativePoints(points);
        return ProfileSnapPoints.Count > 0;
    }

    private async Task SaveRegionAndResumeAsync(LineScanRegion? region)
    {
        try
        {
            var scannerId = ResolveDefaultScannerId();
            var url = ApiBase() + "/api/scanners/" + scannerId + "/roi";
            double[]? bounds = null;
            if (region != null)
            {
                bounds = new[] { region.XMin, region.XMax, region.YMin, region.YMax };
            }

            var json = JsonSerializer.Serialize(new { bounds });
            var response = await LineResultHttp.PostAsync(
                url,
                new StringContent(json, Encoding.UTF8, "application/json")).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                await RunOnUiAsync(() => ScanStatusText = "ROI 未寫入服務，請確認掃描服務已啟動");
                return;
            }

            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            await RunOnUiAsync(() => ApplyScannerSettingsJson(body));
            await ResumeLiveAfterProfileAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            await RunOnUiAsync(() => ScanStatusText = "ROI 未寫入服務，請確認掃描服務已啟動");
        }
    }

    private static string ResolveDefaultScannerId()
    {
        var config = AppConfiguration.Load();
        var id = config["LineScan:DefaultScannerId"];
        if (!string.IsNullOrWhiteSpace(id))
        {
            return id.Trim();
        }

        return "LINE-LIDAR-01";
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

    private async Task StartAsync()
    {
        _userStoppedLive = false;
        try
        {
            var scannerId = ResolveDefaultScannerId();
            var json = JsonSerializer.Serialize(new { scannerId });
            var response = await LineResultHttp.PostAsync(
                ApiBase() + "/api/scan/line/setup/start",
                new StringContent(json, Encoding.UTF8, "application/json")).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                await RunOnUiAsync(() =>
                {
                    ScanStatusText = "無法啟動架設預覽";
                    OverallStatus = SystemStatus.Error;
                });
                return;
            }

            IsScanning = true;
            await RunOnUiAsync(() =>
            {
                LiveHint = "架設預覽";
                ScanStatusText = "即時剖面 · API";
                OverallStatus = SystemStatus.Warning;
            });
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            await RunOnUiAsync(() => ScanStatusText = ex.Message);
        }
    }

    private async Task StopAsync()
    {
        _userStoppedLive = true;
        try
        {
            var scannerId = ResolveDefaultScannerId();
            var json = JsonSerializer.Serialize(new { scannerId });
            await LineResultHttp.PostAsync(
                ApiBase() + "/api/scan/line/setup/stop",
                new StringContent(json, Encoding.UTF8, "application/json")).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
        }

        IsScanning = false;
        LiveHint = "已停止即時剖面";
        ScanStatusText = "已停止";
        OverallStatus = SystemStatus.Normal;
    }

    private void StartLiveWatch()
    {
        _liveTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(400),
        };
        _liveTimer.Tick += OnLiveTick;
        _liveTimer.Start();
        _ = StartAsync();
        _ = RefreshLiveAsync();
    }

    private void OnLiveTick(object? sender, EventArgs e)
    {
        _ = RefreshLiveAsync();
    }

    private async Task RefreshLiveAsync()
    {
        if (_livePollBusy)
        {
            return;
        }

        if (_selectingRegion || _profileFrozen || _isPickingDesktopPoints)
        {
            return;
        }

        _livePollBusy = true;
        try
        {
            var baseUrl = AppConfiguration.Load()["CalibrationMonitor:ApiBaseUrl"];
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                baseUrl = "http://localhost:5295";
            }

            var json = await LineResultHttp.GetStringAsync(baseUrl.TrimEnd('/') + "/api/scan/line/live").ConfigureAwait(false);
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess())
            {
                ApplyLive(json);
            }
            else
            {
                await dispatcher.InvokeAsync(() => ApplyLive(json));
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess())
            {
                LiveHint = "無法讀取 line/live";
                ScanStatusText = ex.Message;
            }
            else
            {
                _ = dispatcher.BeginInvoke(() =>
                {
                    LiveHint = "無法讀取 line/live";
                    ScanStatusText = ex.Message;
                });
            }
        }
        finally
        {
            _livePollBusy = false;
        }
    }

    private void ApplyLive(string json)
    {
        if (_selectingRegion || _profileFrozen)
        {
            return;
        }

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var recording = root.TryGetProperty("recording", out var flag) && flag.ValueKind == JsonValueKind.True;
        _apiLineRecording = recording;
        if (recording)
        {
            LiveHint = "線掃描錄製中";
            _ = RefreshConnectionAsync();
            if (root.TryGetProperty("x", out var xs) && root.TryGetProperty("y", out var ys))
            {
                ApplyNativeLive(xs, ys);
            }

            return;
        }

        _apiLineRecording = false;
        IsScanning = !_userStoppedLive;
        if (root.TryGetProperty("x", out var setupX) && root.TryGetProperty("y", out var setupY))
        {
            if (ApplyNativeLive(setupX, setupY))
            {
                LiveHint = "架設預覽";
                ScanStatusText = "即時剖面 · API";
                OverallStatus = SystemStatus.Normal;
            }
            else
            {
                LiveHint = "等待新的有效截面";
                ScanStatusText = "平均截面尚未就緒";
            }
        }

        _ = RefreshConnectionAsync();
    }

    private void LoadDesignTimeData()
    {
        // 畫面用假資料：待接入線掃描 complete 的角鋼結果
        Materials.Add(new MaterialItem
        {
            Name = "物料 #1",
            Status = ScanStatus.Completed,
            LeftCorner = new CoordinateInfo(0.00, 0.00, 0.000),
            RightCorner = new CoordinateInfo(75.20, 0.00, 0.000),
            TopPoint = new CoordinateInfo(37.60, 75.60, 0.000),
        });
        Materials.Add(new MaterialItem
        {
            Name = "物料 #2",
            Status = ScanStatus.Completed,
            LeftCorner = new CoordinateInfo(120.40, 0.00, 0.000),
            RightCorner = new CoordinateInfo(195.80, 0.00, 0.000),
            TopPoint = new CoordinateInfo(158.10, 75.20, 0.000),
        });
        Materials.Add(new MaterialItem
        {
            Name = "物料 #3",
            Status = ScanStatus.Completed,
            LeftCorner = new CoordinateInfo(240.60, 0.00, 0.000),
            RightCorner = new CoordinateInfo(316.00, 0.00, 0.000),
            TopPoint = new CoordinateInfo(278.30, 75.80, 0.000),
        });

        TotalArea = 71982.400;
        ContourCount = 3;
        LineResults.Add(new MaterialItem
        {
            Name = "角鋼 #1",
            Status = ScanStatus.Completed,
                        Width = 75.2,
                        Height = 75.6,
                        Length = 1200,
                        LeftWidth = 64,
                        RightWidth = 64,
            LeftCorner = new CoordinateInfo(0, 0, 0),
            RightCorner = new CoordinateInfo(75.2, 0, 0),
            TopPoint = new CoordinateInfo(37.6, 0, 75.6),
            TailLeft = new CoordinateInfo(0, 1200, 0),
            TailRight = new CoordinateInfo(75.2, 1200, 0),
            TailTop = new CoordinateInfo(37.6, 1200, 75.6),
        });
        LineResultHeadline = "1 根";
        LineResultStatusText = "設計預覽";
        LineOverallStatus = SystemStatus.Normal;
    }

    private static bool IsInDesignMode()
    {
        return System.ComponentModel.DesignerProperties.GetIsInDesignMode(new DependencyObject());
    }

    private void StartLineResultPolling()
    {
        _linePollTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2),
        };
        _linePollTimer.Tick += OnLineResultPollTick;
        _linePollTimer.Start();
        _ = RefreshLineResultAsync();
    }

    private void OnLineResultPollTick(object? sender, EventArgs e)
    {
        _ = RefreshLineResultAsync();
    }

    private async Task RefreshLineResultAsync()
    {
        if (_linePollBusy)
        {
            return;
        }

        _linePollBusy = true;
        try
        {
            var baseUrl = AppConfiguration.Load()["CalibrationMonitor:ApiBaseUrl"];
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                baseUrl = "http://localhost:5295";
            }

            var url = baseUrl.TrimEnd('/') + "/api/scan/line/latest";
            var json = await LineResultHttp.GetStringAsync(url).ConfigureAwait(false);
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher == null)
            {
                ApplyLineResult(json);
            }
            else
            {
                await dispatcher.InvokeAsync(() => ApplyLineResult(json));
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess())
            {
                ShowLineResultOffline();
            }
            else
            {
                _ = dispatcher.BeginInvoke(ShowLineResultOffline);
            }
        }
        finally
        {
            _linePollBusy = false;
        }
    }

    private void ShowLineResultOffline()
    {
        if (LineResults.Count > 0)
        {
            return;
        }

        LineResultHeadline = "未連線";
        LineResultStatusText = "掃描服務未連線，完成後會顯示在這裡";
        LineOverallStatus = SystemStatus.Warning;
    }

    private void ApplyLineResult(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var status = ReadString(root, "status");
        var taskId = ReadString(root, "taskId");
        var key = taskId + "|" + status;
        if (string.Equals(key, _lineResultKey, StringComparison.Ordinal))
        {
            return;
        }

        _lineResultKey = key;
        if (string.Equals(status, "Completed", StringComparison.OrdinalIgnoreCase))
        {
            FillCompletedLineResult(root, taskId);
            return;
        }

        if (string.Equals(status, "Failed", StringComparison.OrdinalIgnoreCase))
        {
            LineResults.Clear();
            var errorText = "線掃描失敗";
            if (root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
            {
                var code = ReadString(error, "code");
                var message = ReadString(error, "message");
                errorText = string.IsNullOrWhiteSpace(code) ? message : code + " " + message;
            }

            LineResultHeadline = "失敗";
            LineResultStatusText = errorText;
            LineOverallStatus = SystemStatus.Error;
            return;
        }

        if (string.Equals(status, "Processing", StringComparison.OrdinalIgnoreCase))
        {
            LineResultHeadline = "計算中";
            LineResultStatusText = "線掃描完成處理中";
            LineOverallStatus = SystemStatus.Warning;
            return;
        }

        LineResultHeadline = "等待結果";
        LineResultStatusText = "等待線掃描完成";
        LineOverallStatus = SystemStatus.Warning;
    }

    private void FillCompletedLineResult(JsonElement root, string taskId)
    {
        LineResults.Clear();
        var count = 0;
        if (root.TryGetProperty("result", out var result) && result.ValueKind == JsonValueKind.Object)
        {
            if (result.TryGetProperty("detectedCount", out var detected) && detected.TryGetInt32(out var n))
            {
                count = n;
            }

            if (result.TryGetProperty("angleIrons", out var irons) && irons.ValueKind == JsonValueKind.Array)
            {
                foreach (var iron in irons.EnumerateArray())
                {
                    var index = 0;
                    if (iron.TryGetProperty("index", out var indexEl) && indexEl.TryGetInt32(out var parsed))
                    {
                        index = parsed;
                    }

                    var head = ReadObject(iron, "headFeatures");
                    var tail = ReadObject(iron, "tailFeatures");
                    LineResults.Add(new MaterialItem
                    {
                        Name = "角鋼 #" + index,
                        Status = ScanStatus.Completed,
                        Width = ReadDouble(iron, "width"),
                        Height = ReadDouble(iron, "height"),
                        Length = ReadDouble(iron, "length"),
                        LeftWidth = ReadDouble(iron, "leftWidth"),
                        RightWidth = ReadDouble(iron, "rightWidth"),
                        LeftLength = ReadDouble(iron, "leftLength"),
                        RightLength = ReadDouble(iron, "rightLength"),
                        LeftCorner = ReadPoint(head, "leftCorner"),
                        RightCorner = ReadPoint(head, "rightCorner"),
                        TopPoint = ReadPoint(head, "apex"),
                        TailLeft = ReadPoint(tail, "leftCorner"),
                        TailRight = ReadPoint(tail, "rightCorner"),
                        TailTop = ReadPoint(tail, "apex"),
                    });
                }
            }
        }

        if (count == 0)
        {
            count = LineResults.Count;
        }

        LineResultHeadline = count + " 根";
        var shortId = taskId.Length > 8 ? taskId.Substring(0, 8) : taskId;
        LineResultStatusText = string.IsNullOrWhiteSpace(shortId)
            ? "線掃描完成"
            : "線掃描完成 · " + shortId;
        LineOverallStatus = SystemStatus.Normal;
    }

    private static JsonElement ReadObject(JsonElement parent, string name)
    {
        if (parent.ValueKind == JsonValueKind.Object
            && parent.TryGetProperty(name, out var child)
            && child.ValueKind == JsonValueKind.Object)
        {
            return child;
        }

        return default;
    }

    private static CoordinateInfo ReadPoint(JsonElement parent, string name)
    {
        var point = ReadObject(parent, name);
        if (point.ValueKind != JsonValueKind.Object)
        {
            return new CoordinateInfo();
        }

        return new CoordinateInfo(ReadDouble(point, "x"), ReadDouble(point, "y"), ReadDouble(point, "z"));
    }

    private static double ReadDouble(JsonElement parent, string name)
    {
        if (parent.ValueKind == JsonValueKind.Object
            && parent.TryGetProperty(name, out var value)
            && value.TryGetDouble(out var number))
        {
            return number;
        }

        return 0;
    }

    private static string ReadString(JsonElement parent, string name)
    {
        if (parent.ValueKind == JsonValueKind.Object
            && parent.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.String)
        {
            return value.GetString() ?? "";
        }

        return "";
    }
}
