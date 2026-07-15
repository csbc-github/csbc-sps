using System.Windows;
using Microsoft.Extensions.Configuration;
using ThreeDScan.Domain.Abstractions;
using ThreeDScan.ViewModels;
using ThreeDScan.Views;

namespace ThreeDScan;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm = new();
    private ScannerTestScanWindow? _testScanWindow;

    public MainWindow()
    {
        var config = AppConfiguration.Load();

        var calibrationParams = new LjCalibrationParameters();
        var contentsRoot = config["Storage:ContentsRoot"] ?? @"D:\3DScan\contents";
        var isDebugMode = string.Equals(
            config["Calibration:ResponseMode"], "Debug", StringComparison.OrdinalIgnoreCase);
        _vm.ShowCalibrationMonitorTab = isDebugMode;
        _vm.CalibrationMonitorVM = new CalibrationMonitorViewModel(calibrationParams, contentsRoot, isDebugMode);

        InitializeComponent();
        DataContext = _vm;
        Loaded += OnLoadedAsync;
        Closed += OnClosedAsync;
    }

    private async void OnLoadedAsync(object sender, RoutedEventArgs e)
    {
        var config = AppConfiguration.Load();

        if (!_vm.ShowCalibrationMonitorTab) return;
        if (!config.GetValue("CalibrationMonitor:Enabled", true)) return;

        var hubUrl = config["CalibrationMonitor:HubUrl"]
            ?? "http://localhost:5295/hubs/calibration-preview";
        var apiBaseUrl = config["CalibrationMonitor:ApiBaseUrl"];

        try
        {
            await _vm.CalibrationMonitorVM.ConnectAsync(hubUrl, apiBaseUrl);
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
        => await _vm.CalibrationMonitorVM.DisposeAsync();

    private void OnOpenScannerSettings(object sender, RoutedEventArgs e)
    {
        var config = AppConfiguration.Load();
        var window = new ScannerSettingsWindow
        {
            Owner = this,
            DataContext = new ScannerSettingsViewModel(config),
        };
        window.ShowDialog();
    }

    private void OnOpenScannerGroupNames(object sender, RoutedEventArgs e)
    {
        var config = AppConfiguration.Load();
        var window = new ScannerGroupNameWindow
        {
            Owner = this,
            DataContext = new ScannerGroupNameViewModel(config),
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

        var config = AppConfiguration.Load();
        _testScanWindow = new ScannerTestScanWindow
        {
            Owner = this,
            DataContext = new ScannerTestScanViewModel(config),
        };
        _testScanWindow.Closed += (_, _) => _testScanWindow = null;
        _testScanWindow.Show();
    }
}
