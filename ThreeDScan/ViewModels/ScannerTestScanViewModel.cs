using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ThreeDScan.Common;
using ThreeDScan.Domain.Abstractions;
using ThreeDScan.Domain.Models;
using ThreeDScan.Domain.Repositories;

namespace ThreeDScan.ViewModels;

/// <summary>測試掃描視窗 ViewModel：選擇掃描器／群組後觸發單次擷取並推送 3D 預覽。</summary>
public sealed class ScannerTestScanViewModel : ViewModelBase
{
    private readonly IConfiguration _config;

    private string _selectedScannerId = "";
    private ScannerProgramGroup? _selectedGroup;
    private string _statusText = "按「執行測試掃描」以擷取影像並顯示 3D 預覽。";
    private string _geometryText = "";
    private bool _isBusy;

    /// <summary>從 appsettings 載入掃描器清單並初始化命令。</summary>
    public ScannerTestScanViewModel(IConfiguration config)
    {
        _config = config;
        InitializeScannerIds();
        ScanCommand = new RelayCommand(_ => _ = ScanAsync(), _ => CanScan);
    }

    /// <summary>掃描完成後通知視窗開啟 Lj3DView 預覽。</summary>
    public event EventHandler<ScannerCapturePreviewEventArgs>? CapturePreviewReady;

    /// <summary>視窗頂部說明文字。</summary>
    public string HelpText { get; } =
        "選擇掃描器與群組，以該程式已 Save 的設定觸發量測（掃描前會同步至 RUNNING），並以 Lj3DView 顯示 3D 結果。";

    /// <summary>appsettings「Scanners」區段中的 ScannerId 清單。</summary>
    public ObservableCollection<string> ScannerIds { get; } = [];

    /// <summary>目前掃描器的拍攝群組（Scan 1～16）。</summary>
    public ObservableCollection<ScannerProgramGroup> Groups { get; } = [];

    /// <summary>目前選取的掃描器；變更時重新載入群組清單。</summary>
    public string SelectedScannerId
    {
        get
        {
            return _selectedScannerId;
        }
        set
        {
            if (!SetProperty(ref _selectedScannerId, value)) return;
            _ = LoadGroupsAsync();
            RaiseCommandStates();
            RefreshScanTargetLabel();
        }
    }

    /// <summary>目前選取的拍攝群組。</summary>
    public ScannerProgramGroup? SelectedGroup
    {
        get
        {
            return _selectedGroup;
        }
        set
        {
            if (!SetProperty(ref _selectedGroup, value)) return;
            RaiseCommandStates();
            RefreshScanTargetLabel();
        }
    }

    /// <summary>掃描目標顯示（ScannerId · 群組名稱）。</summary>
    public string ScanTargetLabel
    {
        get
        {
            if (SelectedGroup is not { } group || string.IsNullOrWhiteSpace(SelectedScannerId))
                return "（請選擇掃描器與群組）";

            return $"{SelectedScannerId} · {group.ListLabel}";
        }
    }

    /// <summary>狀態列訊息。</summary>
    public string StatusText
    {
        get
        {
            return _statusText;
        }
        private set
        {
            SetProperty(ref _statusText, value);
        }
    }

    /// <summary>掃描結果影像幾何資訊（尺寸、原點、係數）。</summary>
    public string GeometryText
    {
        get
        {
            return _geometryText;
        }
        private set
        {
            SetProperty(ref _geometryText, value);
        }
    }

    /// <summary>是否正在執行測試掃描。</summary>
    public bool IsBusy
    {
        get
        {
            return _isBusy;
        }
        private set
        {
            if (!SetProperty(ref _isBusy, value)) return;
            RaiseCommandStates();
        }
    }

    private bool CanScan
    {
        get
        {
            return !IsBusy && CanOperate;
        }
    }

    private bool CanOperate
    {
        get
        {
            return !string.IsNullOrWhiteSpace(SelectedScannerId) && SelectedGroup is not null;
        }
    }

    /// <summary>觸發單次測試掃描。</summary>
    public ICommand ScanCommand { get; }

    /// <summary>強制更新 <see cref="ScanTargetLabel"/> 綁定。</summary>
    public void RefreshScanTargetLabel()
    {
        OnPropertyChanged(nameof(ScanTargetLabel));
    }

    private void InitializeScannerIds()
    {
        var section = _config.GetSection("Scanners");
        foreach (var child in section.GetChildren())
        {
            if (!string.IsNullOrWhiteSpace(child.Key))
                ScannerIds.Add(child.Key);
        }

        if (ScannerIds.Count > 0)
        {
            SelectedScannerId = ScannerIds[0];
            _ = LoadGroupsAsync();
        }
    }

    private async Task LoadGroupsAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedScannerId)) return;

        try
        {
            var defaultProgramNo = ResolveDefaultProgramNo(SelectedScannerId);
            await ReloadGroupsAndSelectAsync(defaultProgramNo);
        }
        catch (Exception ex)
        {
            StatusText = "群組清單載入失敗";
            MessageBox.Show(ex.Message, "測試掃描", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>將 appsettings 的 NavigatorScanNo（1～16）轉為 programNo（0～15）。</summary>
    private byte ResolveDefaultProgramNo(string scannerId)
    {
        var navScan = _config.GetValue<int?>($"Scanners:{scannerId}:NavigatorScanNo") ?? 1;
        return (byte)Math.Clamp(navScan - 1, 0, 15);
    }

    private async Task ReloadGroupsAndSelectAsync(byte programNo)
    {
        using var scope = WpfServiceProviderFactory.CreateScope(_config);
        var groupRepository = scope.ServiceProvider.GetRequiredService<IScannerProgramGroupRepository>();
        var groups = await groupRepository.GetOrCreateAsync(SelectedScannerId);

        Groups.Clear();
        foreach (var group in groups)
            Groups.Add(group);

        SelectedGroup = Groups.FirstOrDefault(g => g.ProgramNo == programNo) ?? Groups.FirstOrDefault();
    }

    /// <summary>以選定群組的 programNo 觸發擷取，並透過 <see cref="CapturePreviewReady"/> 推送預覽路徑。</summary>
    private async Task ScanAsync()
    {
        if (!CanOperate || SelectedGroup is null) return;

        var scannerId = SelectedScannerId;
        var programNo = SelectedGroup.ProgramNo;

        try
        {
            IsBusy = true;
            StatusText = "正在觸發測試掃描…";
            GeometryText = "";

            using var scope = WpfServiceProviderFactory.CreateScope(_config);
            var captureFactory = scope.ServiceProvider.GetRequiredService<IScannerCaptureFactory>();
            var capture = captureFactory.GetCapture(scannerId);
            var result = await capture.CaptureWithAngleSteelAsync(programNo);

            if (string.IsNullOrWhiteSpace(result.HeightImagePath)
                || string.IsNullOrWhiteSpace(result.GrayImagePath))
                throw new InvalidOperationException("掃描結果缺少影像路徑。");

            if (result.Geometry is { } geo)
            {
                GeometryText =
                    $"尺寸 {geo.Width} × {geo.Height} · " +
                    $"原點 ({geo.OriginX:F3}, {geo.OriginY:F3}) · " +
                    $"係數 X={geo.CoefficientX:F6} Y={geo.CoefficientY:F6}";
            }

            StatusText = string.IsNullOrWhiteSpace(result.PlyPath)
                ? $"掃描完成 · {ScanTargetLabel}"
                : result.AngleDegrees is { } ang
                    ? $"掃描完成 · {ScanTargetLabel} · 角鋼 {result.PlyPointCount:N0} 點 · 夾角 {ang:F2}°\n{result.PlyPath}"
                    : $"掃描完成 · {ScanTargetLabel} · 角鋼 PLY {result.PlyPointCount:N0} 點\n{result.PlyPath}";
            if (!string.IsNullOrWhiteSpace(result.ZipPath))
                StatusText += $"\nZIP：{result.ZipPath}";
            if (!string.IsNullOrWhiteSpace(result.GlbPath))
                StatusText += $"\nGLB：{result.GlbPath}";

            Application.Current.Dispatcher.Invoke(() =>
                CapturePreviewReady?.Invoke(this, new ScannerCapturePreviewEventArgs
                {
                    HeightImagePath = result.HeightImagePath,
                    GrayImagePath = result.GrayImagePath,
                    PlyPath = result.PlyPath,
                    PlyPointCount = result.PlyPointCount,
                }));
        }
        catch (Exception ex)
        {
            StatusText = "測試掃描失敗";
            var detail = ex.GetBaseException().Message;
            if (ex is AggregateException agg && agg.InnerExceptions.Count > 0)
                detail = string.Join("\n", agg.InnerExceptions.Select(e => e.GetBaseException().Message));
            MessageBox.Show(detail, "測試掃描", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            IsBusy = false;
            RefreshScanTargetLabel();
        }
    }

    private void RaiseCommandStates()
    {
        CommandManager.InvalidateRequerySuggested();
    }
}
