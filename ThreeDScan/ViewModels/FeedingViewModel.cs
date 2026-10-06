using System.Collections.ObjectModel;
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
using ThreeDScan.LineScan;
using ThreeDScan.Models;

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
        Timeout = TimeSpan.FromSeconds(5),
    };

    private LidarFrame? _currentFrame;
    private bool _isScanning;
    private bool _userStoppedLive;
    private bool _livePollBusy;
    private string _liveHint = "正在連接線掃描器…";
    private DispatcherTimer? _liveTimer;
    private SickLiveScanner? _live;
    private bool _selectingRegion;
    private LineScanRegion? _sensorRegion;

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
        BeginSelectRegionCommand = new RelayCommand(_ => BeginSelectRegion());
        ClearRegionCommand = new RelayCommand(_ => ClearRegion());

        LoadDesignTimeData();
        if (!IsInDesignMode())
        {
            LineResults.Clear();
            StartLineResultPolling();
            StartLiveWatch();
            _ = LoadRegionAsync();
        }
    }

    private void BeginSelectRegion()
    {
        if (IsSelectingRegion)
        {
            IsSelectingRegion = false;
            ScanStatusText = "已取消框選";
            return;
        }

        if (CurrentFrame == null)
        {
            ScanStatusText = "還沒有剖面，無法框選";
            return;
        }

        IsSelectingRegion = true;
        ScanStatusText = "在左側剖面上拖曳框選範圍";
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
        _ = SaveRegionAsync(region);
    }

    private void ClearRegion()
    {
        IsSelectingRegion = false;
        SensorRegion = null;
        ScanStatusText = "已清除掃描區域";
        _ = SaveRegionAsync(null);
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

    private async Task LoadRegionAsync()
    {
        try
        {
            var response = await LineResultHttp.GetAsync(ApiBase() + "/api/scan/line/region").ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NoContent || !response.IsSuccessStatusCode)
            {
                return;
            }

            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(json))
            {
                return;
            }

            var region = JsonSerializer.Deserialize<LineScanRegion>(json, RegionJson);
            if (region == null || region.XMax <= region.XMin || region.YMax <= region.YMin)
            {
                return;
            }

            await RunOnUiAsync(() => SensorRegion = region);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
        }
    }

    private async Task SaveRegionAsync(LineScanRegion? region)
    {
        try
        {
            var url = ApiBase() + "/api/scan/line/region";
            HttpResponseMessage response;
            if (region == null)
            {
                response = await LineResultHttp.DeleteAsync(url).ConfigureAwait(false);
            }
            else
            {
                var json = JsonSerializer.Serialize(region, RegionJson);
                response = await LineResultHttp.PutAsync(
                    url,
                    new StringContent(json, Encoding.UTF8, "application/json")).ConfigureAwait(false);
            }

            if (!response.IsSuccessStatusCode)
            {
                await RunOnUiAsync(() => ScanStatusText = "掃描區域未寫入服務，請確認掃描服務已啟動");
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            await RunOnUiAsync(() => ScanStatusText = "掃描區域未寫入服務，請確認掃描服務已啟動");
        }
    }

    private static async Task RunOnUiAsync(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess())
        {
            action();
            return;
        }

        await dispatcher.InvokeAsync(action);
    }

    private Task StartAsync()
    {
        _userStoppedLive = false;
        StartLocalLive();
        return Task.CompletedTask;
    }

    private Task StopAsync()
    {
        _userStoppedLive = true;
        StopLocalLive();
        LiveHint = "已停止即時剖面";
        ScanStatusText = "已停止";
        OverallStatus = SystemStatus.Normal;
        return Task.CompletedTask;
    }

    private void StartLiveWatch()
    {
        _liveTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(400),
        };
        _liveTimer.Tick += OnLiveTick;
        _liveTimer.Start();
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

        _livePollBusy = true;
        try
        {
            var baseUrl = AppConfiguration.Load()["CalibrationMonitor:ApiBaseUrl"];
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                baseUrl = "http://localhost:5295";
            }

            var json = await LineResultHttp.GetStringAsync(baseUrl.TrimEnd('/') + "/api/scan/line/live").ConfigureAwait(false);
            var dispatcher = Application.Current?.Dispatcher;
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
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess())
            {
                EnsureLocalLive();
            }
            else
            {
                _ = dispatcher.BeginInvoke(EnsureLocalLive);
            }
        }
        finally
        {
            _livePollBusy = false;
        }
    }

    private void ApplyLive(string json)
    {
        if (_selectingRegion)
        {
            return;
        }

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var recording = root.TryGetProperty("recording", out var flag) && flag.ValueKind == JsonValueKind.True;
        if (recording)
        {
            StopLocalLive();
            LiveHint = "線掃描錄製中";
            if (root.TryGetProperty("x", out var xs) && root.TryGetProperty("y", out var ys))
            {
                var frame = ToRecordedFrame(xs, ys);
                if (frame != null)
                {
                    CurrentFrame = frame;
                }
            }

            return;
        }

        EnsureLocalLive();
    }

    private void EnsureLocalLive()
    {
        if (_userStoppedLive)
        {
            return;
        }

        StartLocalLive();
    }

    private void StartLocalLive()
    {
        if (_live != null)
        {
            return;
        }

        var config = AppConfiguration.Load();
        var ip = config["LineScanner:Ip"];
        if (string.IsNullOrWhiteSpace(ip))
        {
            ip = "192.168.0.1";
        }

        var port = 2111;
        if (int.TryParse(config["LineScanner:Port"], out var parsed) && parsed > 0)
        {
            port = parsed;
        }

        var scanner = new SickLiveScanner();
        scanner.FrameReceived += OnLiveFrame;
        scanner.StatusChanged += OnLiveStatus;
        _live = scanner;
        scanner.Start(ip, port);
        IsScanning = true;
        OverallStatus = SystemStatus.Warning;
    }

    private void StopLocalLive()
    {
        var scanner = _live;
        _live = null;
        if (scanner == null)
        {
            IsScanning = false;
            return;
        }

        scanner.FrameReceived -= OnLiveFrame;
        scanner.StatusChanged -= OnLiveStatus;
        scanner.Dispose();
        IsScanning = false;
    }

    private void OnLiveFrame(object? sender, LidarFrame frame)
    {
        var dispatcher = Application.Current?.Dispatcher;
        void Apply()
        {
            if (_live == null || _selectingRegion)
            {
                return;
            }

            CurrentFrame = frame;
            var valid = 0;
            for (var i = 0; i < frame.Distances.Length; i++)
            {
                var distance = frame.Distances[i];
                if (distance > 1 && distance < 999000)
                {
                    valid++;
                }
            }

            ContourCount = valid;
            LiveHint = "已連線";
            ScanStatusText = "即時剖面 · " + valid + " 點";
            OverallStatus = SystemStatus.Normal;
        }

        if (dispatcher == null || dispatcher.CheckAccess())
        {
            Apply();
        }
        else
        {
            _ = dispatcher.BeginInvoke(Apply);
        }
    }

    private void OnLiveStatus(object? sender, string message)
    {
        var dispatcher = Application.Current?.Dispatcher;
        void Apply()
        {
            if (_live == null)
            {
                return;
            }

            LiveHint = message;
            ScanStatusText = message;
            if (message.Contains("未連線", StringComparison.Ordinal))
            {
                OverallStatus = SystemStatus.Warning;
            }
        }

        if (dispatcher == null || dispatcher.CheckAccess())
        {
            Apply();
        }
        else
        {
            _ = dispatcher.BeginInvoke(Apply);
        }
    }

    private static LidarFrame? ToRecordedFrame(JsonElement xs, JsonElement ys)
    {
        if (xs.ValueKind != JsonValueKind.Array || ys.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var count = Math.Min(xs.GetArrayLength(), ys.GetArrayLength());
        var samples = new List<(double Angle, int Distance)>();
        for (var i = 0; i < count; i++)
        {
            var x = xs[i].GetDouble();
            var y = ys[i].GetDouble();
            var distance = Math.Sqrt(x * x + y * y);
            if (distance <= 1)
            {
                continue;
            }

            var angle = Math.Atan2(y, x) * 180.0 / Math.PI;
            samples.Add((angle, (int)Math.Round(distance)));
        }

        if (samples.Count < 2)
        {
            return null;
        }

        samples.Sort((left, right) => left.Angle.CompareTo(right.Angle));
        var steps = new List<double>();
        for (var i = 1; i < samples.Count; i++)
        {
            var step = samples[i].Angle - samples[i - 1].Angle;
            if (step > 0.001)
            {
                steps.Add(step);
            }
        }

        if (steps.Count == 0)
        {
            return null;
        }

        steps.Sort();
        var resolution = steps[steps.Count / 2];
        var start = samples[0].Angle;
        var bins = (int)Math.Round((samples[samples.Count - 1].Angle - start) / resolution) + 1;
        if (bins < 2 || bins > 20000)
        {
            return null;
        }

        var distances = new int[bins];
        foreach (var sample in samples)
        {
            var index = (int)Math.Round((sample.Angle - start) / resolution);
            if (index >= 0 && index < bins)
            {
                distances[index] = sample.Distance;
            }
        }

        return new LidarFrame(0, -start, resolution, distances);
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
            var dispatcher = Application.Current?.Dispatcher;
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
            var dispatcher = Application.Current?.Dispatcher;
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
