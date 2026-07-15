using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ThreeDScan.Common;
using ThreeDScan.Domain.Abstractions;
using ThreeDScan.Domain.Models;

namespace ThreeDScan.ViewModels;

/// <summary>群組名稱視窗 ViewModel：編輯各 Scan 群組的顯示名稱並寫入資料庫。</summary>
public sealed class ScannerGroupNameViewModel : ViewModelBase
{
    private readonly IConfiguration _config;

    private string _selectedScannerId = "";
    private ScannerProgramGroup? _selectedGroup;
    private string _editableGroupName = "";
    private string _statusText = "選擇掃描器與群組，編輯名稱後按「更新名稱」。";
    private bool _isBusy;

    /// <summary>從 appsettings 載入掃描器清單並初始化命令。</summary>
    public ScannerGroupNameViewModel(IConfiguration config)
    {
        _config = config;

        var section = config.GetSection("Scanners");
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

        SaveGroupNameCommand = new RelayCommand(_ => _ = SaveGroupNameAsync(), _ => CanSave);
    }

    /// <summary>appsettings「Scanners」區段中的 ScannerId 清單。</summary>
    public ObservableCollection<string> ScannerIds { get; } = [];

    /// <summary>目前掃描器的拍攝群組（Scan 1～16）。</summary>
    public ObservableCollection<ScannerProgramGroup> Groups { get; } = [];

    /// <summary>目前選取的掃描器；變更時重新載入群組清單。</summary>
    public string SelectedScannerId
    {
        get => _selectedScannerId;
        set
        {
            if (!SetProperty(ref _selectedScannerId, value)) return;
            _ = LoadGroupsAsync();
            RaiseCommandStates();
        }
    }

    /// <summary>目前選取的拍攝群組；變更時同步 <see cref="EditableGroupName"/>。</summary>
    public ScannerProgramGroup? SelectedGroup
    {
        get => _selectedGroup;
        set
        {
            if (!SetProperty(ref _selectedGroup, value)) return;
            EditableGroupName = value?.DisplayName ?? "";
            OnPropertyChanged(nameof(SelectedGroupApiHint));
            RaiseCommandStates();
        }
    }

    /// <summary>提示呼叫校正 API 時應帶入的 navigatorScanNo。</summary>
    public string SelectedGroupApiHint => SelectedGroup is null
        ? ""
        : $"API 請帶 navigatorScanNo：{SelectedGroup.NavigatorScanNo}";

    /// <summary>使用者編輯中的群組顯示名稱。</summary>
    public string EditableGroupName
    {
        get => _editableGroupName;
        set
        {
            if (SetProperty(ref _editableGroupName, value))
                RaiseCommandStates();
        }
    }

    /// <summary>狀態列訊息。</summary>
    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    /// <summary>是否正在儲存群組名稱。</summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetProperty(ref _isBusy, value)) return;
            RaiseCommandStates();
        }
    }

    private bool CanSave => !IsBusy
        && !string.IsNullOrWhiteSpace(SelectedScannerId)
        && SelectedGroup is not null
        && !string.IsNullOrWhiteSpace(EditableGroupName)
        && !string.Equals(EditableGroupName.Trim(), SelectedGroup.DisplayName, StringComparison.Ordinal);

    /// <summary>將 <see cref="EditableGroupName"/> 寫入資料庫。</summary>
    public ICommand SaveGroupNameCommand { get; }

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
            MessageBox.Show(ex.Message, "群組名稱", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>將 appsettings 的 NavigatorScanNo（1～16）轉為 programNo（0～15）。</summary>
    private byte ResolveDefaultProgramNo(string scannerId)
    {
        var navScan = _config.GetValue<int?>($"Scanners:{scannerId}:NavigatorScanNo") ?? 1;
        return (byte)Math.Clamp(navScan - 1, 0, 15);
    }

    /// <summary>更新群組顯示名稱並重新載入清單。</summary>
    private async Task SaveGroupNameAsync()
    {
        if (SelectedGroup is null) return;

        var trimmed = EditableGroupName.Trim();
        if (string.IsNullOrWhiteSpace(trimmed)) return;

        var programNo = SelectedGroup.ProgramNo;

        try
        {
            IsBusy = true;
            using var scope = WpfServiceProviderFactory.CreateScope(_config);
            var groupStore = scope.ServiceProvider.GetRequiredService<IScannerProgramGroupStore>();
            await groupStore.UpdateDisplayNameAsync(
                SelectedScannerId,
                programNo,
                trimmed,
                Environment.UserName);

            await ReloadGroupsAndSelectAsync(programNo);
            StatusText = $"已更新群組名稱：{trimmed}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "群組名稱", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ReloadGroupsAndSelectAsync(byte programNo)
    {
        using var scope = WpfServiceProviderFactory.CreateScope(_config);
        var groupStore = scope.ServiceProvider.GetRequiredService<IScannerProgramGroupStore>();
        var groups = await groupStore.GetOrCreateAsync(SelectedScannerId);

        Groups.Clear();
        foreach (var group in groups)
            Groups.Add(group);

        SelectedGroup = Groups.FirstOrDefault(g => g.ProgramNo == programNo) ?? Groups.FirstOrDefault();
    }

    private void RaiseCommandStates() => CommandManager.InvalidateRequerySuggested();
}
