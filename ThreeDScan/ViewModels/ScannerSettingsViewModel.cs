using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ThreeDScan.Common;
using ThreeDScan.Domain.Abstractions;
using ThreeDScan.Domain.Models;

namespace ThreeDScan.ViewModels;

/// <summary>
/// 掃描器拍攝參數視窗 ViewModel：掃描器／群組選擇、讀寫 LJ 控制器與資料庫備份。
/// 表單欄位委派給 <see cref="Form"/>；非同步操作以 <see cref="SemaphoreSlim"/> 防重入。
/// </summary>
public sealed class ScannerSettingsViewModel : ViewModelBase
{
    private const string DialogTitle = "掃描參數";

    private readonly IConfiguration _config;
    private readonly SemaphoreSlim _operationLock = new(1, 1);

    private string _selectedScannerId = "";
    private ScannerProgramGroup? _selectedGroup;
    private string _statusText = "請選擇掃描器與群組；選定群組後會自動從控制器讀取。";
    private bool _isBusy;
    private bool _hasDatabaseBackup;

    /// <summary>載入群組清單並重設 <see cref="SelectedGroup"/> 時，暫停自動觸發讀取。</summary>
    private bool _suppressGroupAutoLoad;

    /// <summary>從 appsettings 載入掃描器清單並初始化命令。</summary>
    public ScannerSettingsViewModel(IConfiguration config)
    {
        _config = config;
        InitializeScannerIds();

        LoadFromControllerCommand = new RelayCommand(_ => _ = LoadFromControllerAsync(), _ => CanOperate);
        SaveToControllerCommand = new RelayCommand(_ => _ = SaveToControllerAsync(), _ => CanOperate);
        RestoreFromDatabaseCommand = new RelayCommand(_ => _ = RestoreFromDatabaseAsync(), _ => CanRestoreFromDatabase);
    }

    /// <summary>拍攝參數表單（感光、光量、峰值等），綁定 XAML <c>Form.*</c> 欄位。</summary>
    public ScannerCaptureSettingsFormViewModel Form { get; } = new();

    /// <summary>appsettings「Scanners」區段中的 ScannerId 清單。</summary>
    public ObservableCollection<string> ScannerIds { get; } = [];

    /// <summary>目前掃描器的拍攝群組（Scan 1～16）。</summary>
    public ObservableCollection<ScannerProgramGroup> Groups { get; } = [];

    /// <summary>Y 方向數據間隔唯讀顯示（來自 appsettings，不可在此視窗調整）。</summary>
    public string YDataIntervalDisplay
    {
        get
        {
            var mm = _config.GetValue<double?>($"Scanners:{SelectedScannerId}:YDataIntervalMm") ?? 0.8;
            return $"{mm:0.0} mm（Navigator 顯示值，不可調整）";
        }
    }

    /// <summary>目前選取的掃描器；變更時重新載入群組清單。</summary>
    public string SelectedScannerId
    {
        get => _selectedScannerId;
        set
        {
            if (!SetProperty(ref _selectedScannerId, value)) return;
            OnPropertyChanged(nameof(YDataIntervalDisplay));
            if (!IsBusy)
                _ = LoadGroupsAsync();
            RaiseCommandStates();
        }
    }

    /// <summary>目前選取的拍攝群組；變更時自動從控制器讀取參數（除非 <see cref="_suppressGroupAutoLoad"/>）。</summary>
    public ScannerProgramGroup? SelectedGroup
    {
        get => _selectedGroup;
        set
        {
            if (!SetProperty(ref _selectedGroup, value)) return;
            RaiseCommandStates();
            if (_suppressGroupAutoLoad)
                return;

            if (value is not null && !string.IsNullOrEmpty(SelectedScannerId))
                _ = LoadGroupContextAsync();
            else
                HasDatabaseBackup = false;
        }
    }

    /// <summary>忙碌中時停用整個表單。</summary>
    public bool IsFormEnabled => !IsBusy;

    /// <summary>狀態列訊息（載入中、成功、失敗提示）。</summary>
    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    /// <summary>是否正在執行非同步操作（顯示遮罩並停用表單）。</summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetProperty(ref _isBusy, value)) return;
            OnPropertyChanged(nameof(IsFormEnabled));
            RaiseCommandStates();
        }
    }

    /// <summary>目前群組是否已有資料庫備份（決定「從資料庫還原」是否可用）。</summary>
    public bool HasDatabaseBackup
    {
        get => _hasDatabaseBackup;
        private set
        {
            if (!SetProperty(ref _hasDatabaseBackup, value)) return;
            RaiseCommandStates();
        }
    }

    private bool CanOperate => !IsBusy
        && !string.IsNullOrWhiteSpace(SelectedScannerId)
        && SelectedGroup is not null;

    private bool CanRestoreFromDatabase => CanOperate && HasDatabaseBackup;

    private string GroupLabel => SelectedGroup?.ListLabel ?? "（未選群組）";

    /// <summary>手動從 LJ 控制器重新讀取目前群組參數至表單。</summary>
    public ICommand LoadFromControllerCommand { get; }

    /// <summary>將表單參數 Save 至控制器並寫入資料庫備份。</summary>
    public ICommand SaveToControllerCommand { get; }

    /// <summary>從資料庫備份載入參數至表單（不寫入控制器）。</summary>
    public ICommand RestoreFromDatabaseCommand { get; }

    /// <summary>從 appsettings 填入 <see cref="ScannerIds"/> 並選取第一台掃描器。</summary>
    private void InitializeScannerIds()
    {
        foreach (var child in _config.GetSection("Scanners").GetChildren())
        {
            if (!string.IsNullOrWhiteSpace(child.Key))
                ScannerIds.Add(child.Key);
        }

        if (ScannerIds.Count == 0) return;

        SelectedScannerId = ScannerIds[0];
        _ = LoadGroupsAsync();
    }

    /// <summary>切換掃描器時：載入群組清單、選預設群組、再讀取控制器參數。</summary>
    private async Task LoadGroupsAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedScannerId)) return;

        await RunExclusiveAsync("正在載入群組清單…", async () =>
        {
            var defaultProgramNo = ResolveDefaultProgramNo(SelectedScannerId);

            _suppressGroupAutoLoad = true;
            try
            {
                await ReloadGroupsAsync(defaultProgramNo);
            }
            finally
            {
                _suppressGroupAutoLoad = false;
            }

            await LoadGroupContextCoreAsync();
        });
    }

    /// <summary>從資料庫取得群組名稱並更新 <see cref="Groups"/>／<see cref="SelectedGroup"/>。</summary>
    private async Task ReloadGroupsAsync(byte defaultProgramNo)
    {
        using var scope = WpfServiceProviderFactory.CreateScope(_config);
        var groupStore = scope.ServiceProvider.GetRequiredService<IScannerProgramGroupStore>();
        var groups = await groupStore.GetOrCreateAsync(SelectedScannerId);

        Groups.Clear();
        foreach (var group in groups)
            Groups.Add(group);

        SelectedGroup = Groups.FirstOrDefault(g => g.ProgramNo == defaultProgramNo) ?? Groups.FirstOrDefault();
    }

    /// <summary>切換群組時：讀取備份狀態與控制器參數。</summary>
    private async Task LoadGroupContextAsync()
        => await RunExclusiveAsync("正在載入群組資料…", LoadGroupContextCoreAsync);

    /// <summary>查詢資料庫是否有 Blob 備份，並從控制器讀取參數填入 <see cref="Form"/>。</summary>
    private async Task LoadGroupContextCoreAsync()
    {
        if (!TryGetProgramNo(out var programNo))
        {
            StatusText = "請先選擇群組。";
            return;
        }

        await WithSettingsServiceAsync(async service =>
        {
            var blob = await service.GetStoredBlobAsync(SelectedScannerId, programNo);
            HasDatabaseBackup = blob is { Length: > 0 };

            var settings = await service.ReadFromControllerAsync(SelectedScannerId, programNo);
            Form.LoadFrom(settings);
            StatusText = $"已讀取 {SelectedScannerId} · {GroupLabel}。";
        });
    }

    private async Task LoadFromControllerAsync()
        => await RunExclusiveAsync("正在從控制器讀取…", LoadGroupContextCoreAsync);

    /// <summary>確認後將 <see cref="Form"/> 寫入控制器並同步資料庫。</summary>
    private async Task SaveToControllerAsync()
    {
        if (!TryGetProgramNo(out var programNo))
        {
            WpfMessageBox.ShowWarning("請先選擇群組。", DialogTitle);
            return;
        }

        if (!ConfirmSave()) return;

        await RunExclusiveAsync("正在寫入控制器並 Save…", async () =>
        {
            await WithSettingsServiceAsync(async service =>
            {
                await service.SaveToControllerAsync(
                    SelectedScannerId,
                    programNo,
                    Form.ToSettings(programNo, ResolveYDataIntervalMm()),
                    updatedBy: Environment.UserName);
            });

            StatusText = $"已 Save {SelectedScannerId} · {GroupLabel}，並同步至資料庫。";
            HasDatabaseBackup = true;
            ShowSaveSuccessMessage();
        });
    }

    /// <summary>從資料庫備份還原至表單（需使用者確認）。</summary>
    private async Task RestoreFromDatabaseAsync()
    {
        if (!HasDatabaseBackup)
        {
            WpfMessageBox.ShowInfo(
                $"{SelectedScannerId} · {GroupLabel} 尚無資料庫備份。\n\n請先按「套用並 Save」建立備份。",
                DialogTitle);
            return;
        }

        if (!TryGetProgramNo(out var programNo))
        {
            WpfMessageBox.ShowWarning("請先選擇群組。", DialogTitle);
            return;
        }

        if (!ConfirmRestore()) return;

        await RunExclusiveAsync("正在從資料庫載入…", async () =>
        {
            await WithSettingsServiceAsync(async service =>
            {
                var settings = await service.ReadFromDatabaseAsync(SelectedScannerId, programNo);
                Form.LoadFrom(settings);
            });

            StatusText = $"已從資料庫載入 {SelectedScannerId} · {GroupLabel}（尚未寫入攝影機）。";
            WpfMessageBox.ShowInfo("已從資料庫載入至畫面。\n要寫入攝影機請按「套用並 Save」。", DialogTitle);
        });
    }

    /// <summary>建立 DI scope 並執行 <see cref="IScannerProgramSettingsService"/> 操作。</summary>
    private async Task WithSettingsServiceAsync(Func<IScannerProgramSettingsService, Task> action)
    {
        using var scope = WpfServiceProviderFactory.CreateScope(_config);
        var service = scope.ServiceProvider.GetRequiredService<IScannerProgramSettingsService>();
        await action(service);
    }

    /// <summary>序列化非同步操作：忙碌中略過新請求，失敗時顯示訊息框。</summary>
    private async Task RunExclusiveAsync(string busyStatus, Func<Task> action)
    {
        if (!await _operationLock.WaitAsync(0))
            return;

        try
        {
            IsBusy = true;
            StatusText = busyStatus;
            await action();
        }
        catch (Exception ex)
        {
            StatusText = "操作失敗";
            WpfMessageBox.ShowError(ex.Message, DialogTitle);
        }
        finally
        {
            IsBusy = false;
            _operationLock.Release();
        }
    }

    /// <summary>從 <see cref="SelectedGroup"/> 取得 LJ SDK programNo。</summary>
    private bool TryGetProgramNo(out byte programNo)
    {
        if (SelectedGroup is null)
        {
            programNo = 0;
            return false;
        }

        programNo = SelectedGroup.ProgramNo;
        return true;
    }

    /// <summary>將 appsettings 的 NavigatorScanNo（1～16）轉為 programNo（0～15）。</summary>
    private byte ResolveDefaultProgramNo(string scannerId)
    {
        var navScan = _config.GetValue<int?>($"Scanners:{scannerId}:NavigatorScanNo") ?? 1;
        return (byte)Math.Clamp(navScan - 1, 0, 15);
    }

    /// <summary>讀取目前掃描器的 Y 方向數據間隔（mm）。</summary>
    private double ResolveYDataIntervalMm()
        => _config.GetValue<double?>($"Scanners:{SelectedScannerId}:YDataIntervalMm") ?? 0.8;

    /// <summary>Save 前確認 Navigator 未連線，避免設定互相覆蓋。</summary>
    private bool ConfirmSave()
    {
        return MessageBox.Show(
                   $"將寫入 {SelectedScannerId} · {GroupLabel} 並 Save 至控制器，同時備份至資料庫。\n\n" +
                   "請確認 Navigator 未連線中，避免互相覆蓋。",
                   DialogTitle,
                   MessageBoxButton.OKCancel,
                   MessageBoxImage.Question)
               == MessageBoxResult.OK;
    }

    /// <summary>還原前確認僅更新畫面、不寫入攝影機。</summary>
    private bool ConfirmRestore()
    {
        return MessageBox.Show(
                   $"將從資料庫載入 {SelectedScannerId} · {GroupLabel} 至畫面。\n\n" +
                   "此操作不會寫入攝影機；要套用至控制器請再按「套用並 Save」。\n\n是否繼續？",
                   "還原掃描參數",
                   MessageBoxButton.OKCancel,
                   MessageBoxImage.Question)
               == MessageBoxResult.OK;
    }

    /// <summary>Save 成功提示；非手動光量模式時附加曝光說明。</summary>
    private void ShowSaveSuccessMessage()
    {
        var exposureHint = Form.LightControlMode == 0
            ? ""
            : "\n\n注意：光量控制為自動/SLOPE 時，曝光時間由控制器自動調整。若要手動控制曝光，請先設為「手動」。";
        WpfMessageBox.ShowInfo("掃描參數已寫入控制器並備份。" + exposureHint, DialogTitle);
    }

    private void RaiseCommandStates() => CommandManager.InvalidateRequerySuggested();
}
