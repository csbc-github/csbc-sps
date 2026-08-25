using System.Windows;
using Microsoft.Extensions.Configuration;
using ThreeDScan.Auth;
using ThreeDScan.Domain.Models;
using ThreeDScan.ViewModels;
using ThreeDScan.Views;

namespace ThreeDScan;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm = new();
    private readonly IConfigurationRoot _config;
    private ScannerTestScanWindow? _testScanWindow;
    private IAccessTokenSource? _tokenSource;

    public MainWindow()
    {
        _config = AppConfiguration.Load();

        var calibrationParams = new LjCalibrationParameters();
        var contentsRoot = _config["Storage:ContentsRoot"] ?? "";
        var isDebugMode = string.Equals(
            _config["Calibration:ResponseMode"], "Debug", StringComparison.OrdinalIgnoreCase);
        _vm.ShowCalibrationMonitorTab = isDebugMode;
        _vm.CalibrationMonitorVM = new CalibrationMonitorViewModel(calibrationParams, contentsRoot, isDebugMode);

        InitializeComponent();
        DataContext = _vm;
        Loaded += OnLoadedAsync;
        Closed += OnClosedAsync;
    }

    /// <summary>
    /// 視窗載入後連線校正監看 Hub。
    /// 若 Authentication 已啟用，依 Flow 提供 SignalR <c>AccessTokenProvider</c>
    ///（Interactive 會 Refresh；ClientCredentials 會重新換票）。
    /// </summary>
    private async void OnLoadedAsync(object sender, RoutedEventArgs e)
    {
        if (!_vm.ShowCalibrationMonitorTab) return;
        if (!_config.GetValue("CalibrationMonitor:Enabled", true)) return;

        var hubUrl = _config["CalibrationMonitor:HubUrl"]
            ?? "http://localhost:5295/hubs/calibration-preview";
        var apiBaseUrl = _config["CalibrationMonitor:ApiBaseUrl"];

        // 啟用 IdP 時，Hub 連線需帶 Bearer（Api 端從 query access_token 取出）
        Func<Task<string?>>? accessTokenFactory = null;
        var auth = _config.GetSection(AuthenticationOptions.SectionName).Get<AuthenticationOptions>();
        if (auth?.Enabled == true)
        {
            _tokenSource ??= AuthTokenServiceFactory.Create(auth);
            accessTokenFactory = () => _tokenSource.GetAccessTokenAsync();
        }

        try
        {
            await _vm.CalibrationMonitorVM.ConnectAsync(hubUrl, apiBaseUrl, accessTokenFactory);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"無法連線校正監看 Hub：{hubUrl}\n\n{ex.Message}",
                "校正監看",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private async void OnClosedAsync(object? sender, EventArgs e)
    {
        await _vm.CalibrationMonitorVM.DisposeAsync();
    }

    private void OnOpenScannerSettings(object sender, RoutedEventArgs e)
    {
        var window = new ScannerSettingsWindow
        {
            Owner = this,
            DataContext = new ScannerSettingsViewModel(_config),
        };
        window.ShowDialog();
    }

    private void OnOpenScannerGroupNames(object sender, RoutedEventArgs e)
    {
        var window = new ScannerGroupNameWindow
        {
            Owner = this,
            DataContext = new ScannerGroupNameViewModel(_config),
        };
        window.ShowDialog();
    }

    private void OnOpenScannerTestScan(object sender, RoutedEventArgs e)
    {
        if (_testScanWindow is { IsLoaded: true })
        {
            _testScanWindow.Activate();
            if (_testScanWindow.DataContext is ScannerTestScanViewModel vm)
                vm.RefreshScanTargetLabel();
            return;
        }

        _testScanWindow = new ScannerTestScanWindow
        {
            Owner = this,
            DataContext = new ScannerTestScanViewModel(_config),
        };
        _testScanWindow.Closed += (_, _) => _testScanWindow = null;
        _testScanWindow.Show();
    }
}
