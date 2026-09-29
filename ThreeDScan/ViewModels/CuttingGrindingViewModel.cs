using ThreeDScan.Common;

namespace ThreeDScan.ViewModels;

// 割切研磨區（容器）VM：對應 SD §10.6 UI-04，割切／研磨為真正的子 Tab
public class CuttingGrindingViewModel : ViewModelBase
{
    private int _selectedSubTabIndex;

    public CuttingViewModel CuttingVM { get; } = new();
    public GrindingViewModel GrindingVM { get; } = new();

    public int SelectedSubTabIndex
    {
        get
        {
            return _selectedSubTabIndex;
        }
        set
        {
            SetProperty(ref _selectedSubTabIndex, value);
        }
    }
}
