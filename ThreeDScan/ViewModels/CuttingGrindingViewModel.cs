using ThreeDScan.Common;

namespace ThreeDScan.ViewModels;

// 割切研磨區（容器）VM：對應 SD §10.6 UI-04 畫面，內含割切／研磨子 Tab
public class CuttingGrindingViewModel : ViewModelBase
{
    private bool _isCuttingTab = true;

    public CuttingViewModel CuttingVM { get; } = new();
    public GrindingViewModel GrindingVM { get; } = new();

    public bool IsCuttingTab { get
    {
        return _isCuttingTab;
    } set { if (SetProperty(ref _isCuttingTab, value)) OnPropertyChanged(nameof(IsGrindingTab)); } }
    public bool IsGrindingTab { get
    {
        return !_isCuttingTab;
    } set { if (SetProperty(ref _isCuttingTab, !value)) OnPropertyChanged(nameof(IsCuttingTab)); } }
}
