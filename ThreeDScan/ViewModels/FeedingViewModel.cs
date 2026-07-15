using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using LMX;
using ThreeDScan.Common;
using ThreeDScan.Models;

namespace ThreeDScan.ViewModels;

// 進料區 VM：對應 SA UC-03 線掃描（角鋼三特徵點）／ SD §10.2
public class FeedingViewModel : ViewModelBase
{
    // TODO: 之後抽到設定檔；目前先比照 LMX Demo 預設值。
    private const string LidarIp = "192.168.0.10";
    private const int LidarPort = 2112;

    private double _totalArea;
    private int _contourCount;
    private string _scanStatusText = "就緒 — 等待開始掃描";
    private SystemStatus _overallStatus = SystemStatus.Normal;

    private LidarFrame? _currentFrame;
    private bool _isScanning;

    private LidarService? _svc;

    // 剖面圖校正（綁定到 LidarScanView；之後可改為設定檔）
    private string _lidarCalibAngleMinDeg = "";
    private string _lidarCalibAngleMaxDeg = "";
    private string _lidarCalibAngleCorrectionDeg = "0";
    private string _lidarCalibXOffsetMm = "0";
    private string _lidarCalibZOffsetMm = "0";
    private string _lidarCalibReferenceYMm = "";

    public ObservableCollection<MaterialItem> Materials { get; } = new();

    public double TotalArea { get => _totalArea; set => SetProperty(ref _totalArea, value); }
    public int ContourCount { get => _contourCount; set => SetProperty(ref _contourCount, value); }
    public string ScanStatusText { get => _scanStatusText; set => SetProperty(ref _scanStatusText, value); }

    // 進料區整體狀態燈（綠/黃/紅），對應 SA NFR-U-01
    public SystemStatus OverallStatus { get => _overallStatus; set => SetProperty(ref _overallStatus, value); }

    public LidarFrame? CurrentFrame { get => _currentFrame; set => SetProperty(ref _currentFrame, value); }

    public bool IsScanning
    {
        get => _isScanning;
        set
        {
            if (SetProperty(ref _isScanning, value))
                CommandManager.InvalidateRequerySuggested();
        }
    }

    public ICommand StartScanCommand { get; }
    public ICommand StopScanCommand { get; }

    /// <summary>角度下限（°），空白＝不限制。例：只取朝下方時可填 70。</summary>
    public string LidarCalibAngleMinDeg { get => _lidarCalibAngleMinDeg; set => SetProperty(ref _lidarCalibAngleMinDeg, value); }

    /// <summary>角度上限（°），空白＝不限制。例：110。</summary>
    public string LidarCalibAngleMaxDeg { get => _lidarCalibAngleMaxDeg; set => SetProperty(ref _lidarCalibAngleMaxDeg, value); }

    /// <summary>角度平移（°），加在公式 θ = i·解析度 − 起始角 之後。</summary>
    public string LidarCalibAngleCorrectionDeg { get => _lidarCalibAngleCorrectionDeg; set => SetProperty(ref _lidarCalibAngleCorrectionDeg, value); }

    /// <summary>X 平移（mm），畫面與物料區 X 一併平移。</summary>
    public string LidarCalibXOffsetMm { get => _lidarCalibXOffsetMm; set => SetProperty(ref _lidarCalibXOffsetMm, value); }

    /// <summary>Z 平移（mm），整條剖面上下平移。</summary>
    public string LidarCalibZOffsetMm { get => _lidarCalibZOffsetMm; set => SetProperty(ref _lidarCalibZOffsetMm, value); }

    /// <summary>參考面 Y（mm，雷達座標），空白＝自動用有效點 Y 的中位數。</summary>
    public string LidarCalibReferenceYMm { get => _lidarCalibReferenceYMm; set => SetProperty(ref _lidarCalibReferenceYMm, value); }

    public FeedingViewModel()
    {
        StartScanCommand = new RelayCommand(_ => _ = StartAsync(), _ => !IsScanning);
        StopScanCommand = new RelayCommand(_ => _ = StopAsync(), _ => IsScanning);

        LoadDesignTimeData();
    }

    private async Task StartAsync()
    {
        if (_svc != null) return;

        // 走實機 UDP；若沒實機可改成 mockMode: true 用內建模擬資料驗收 UI。
        _svc = new LidarService(LidarIp, LidarPort, mockMode: false);
        _svc.FrameReceived += OnFrameReceived;
        _svc.ErrorOccurred += OnError;

        ScanStatusText = "連線中…";
        OverallStatus = SystemStatus.Warning;

        var ok = await _svc.StartAsync();
        if (ok)
        {
            IsScanning = true;
            ScanStatusText = "掃描中";
            OverallStatus = SystemStatus.Normal;
        }
        else
        {
            ScanStatusText = "連線失敗";
            OverallStatus = SystemStatus.Error;
            await DisposeServiceAsync();
        }
    }

    private async Task StopAsync()
    {
        await DisposeServiceAsync();
        IsScanning = false;
        ScanStatusText = "已停止";
        OverallStatus = SystemStatus.Normal;
    }

    private async Task DisposeServiceAsync()
    {
        if (_svc == null) return;
        _svc.FrameReceived -= OnFrameReceived;
        _svc.ErrorOccurred -= OnError;
        try { await _svc.StopAsync(); } catch { /* swallow */ }
        _svc.Dispose();
        _svc = null;
    }

    private void OnFrameReceived(object? sender, LidarFrame frame)
    {
        // 事件來自背景執行緒；UI binding 必須回到 UI thread 才能更新 DependencyProperty。
        void Apply()
        {
            CurrentFrame = frame;

            // 即時統計：協助診斷實機 vs. mock 的差異。
            // 距離 0 / 1 / 999999 都視為「無效」，跑完 30 幀後可以看到典型值分佈。
            int n = frame.Distances.Length;
            int distMin = int.MaxValue, distMax = int.MinValue, valid = 0;
            long sum = 0;
            double xMin = double.MaxValue, xMax = double.MinValue;
            double yMin = double.MaxValue, yMax = double.MinValue;
            for (int i = 0; i < n; i++)
            {
                int d = frame.Distances[i];
                if (d > 1 && d < 999000)
                {
                    valid++;
                    sum += d;
                    if (d < distMin) distMin = d;
                    if (d > distMax) distMax = d;
                }
                var (x, y) = frame.Cartesian[i];
                if (x < xMin) xMin = x; if (x > xMax) xMax = x;
                if (y < yMin) yMin = y; if (y > yMax) yMax = y;
            }
            int distAvg = valid > 0 ? (int)(sum / valid) : 0;
            int invalid = n - valid;

            ScanStatusText = valid == 0
                ? $"幀#{frame.FrameNumber} · {n}點 · 全部無效"
                : $"幀#{frame.FrameNumber} · {n}點 (有效{valid}/無效{invalid}) · " +
                  $"距離 {distMin}~{distMax} 平均{distAvg} mm · " +
                  $"X[{xMin:F0}~{xMax:F0}] Y[{yMin:F0}~{yMax:F0}] mm · " +
                  $"角度解析 {frame.AngleResolutionDeg:F3}°";
            ContourCount = valid;
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess()) Apply();
        else dispatcher.BeginInvoke(Apply);
    }

    private void OnError(object? sender, string message)
    {
        var dispatcher = Application.Current?.Dispatcher;
        void Apply()
        {
            ScanStatusText = "雷達異常：" + message;
            OverallStatus = SystemStatus.Error;
        }
        if (dispatcher == null || dispatcher.CheckAccess()) Apply();
        else dispatcher.BeginInvoke(Apply);
    }

    private void LoadDesignTimeData()
    {
        // 畫面用假資料：待接入 UC-03 之 /api/scan/line 回應
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
    }
}
