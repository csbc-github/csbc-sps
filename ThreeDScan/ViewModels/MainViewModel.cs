using ThreeDScan.Common;

namespace ThreeDScan.ViewModels;

// 主視窗 VM：Tab 切換（SD §10.6 UI-02~UI-04）
public class MainViewModel : ViewModelBase
{
    private int _selectedTabIndex;

    public FeedingViewModel FeedingVM { get; } = new();
    public PrintingViewModel PrintingVM { get; } = new();
    public CuttingGrindingViewModel CuttingGrindingVM { get; } = new();
    public CalibrationMonitorViewModel CalibrationMonitorVM { get; set; } = null!;

    private bool _showCalibrationMonitorTab;

    /// <summary>僅 <c>Calibration:ResponseMode=Debug</c> 時顯示校正監看 Tab。</summary>
    public bool ShowCalibrationMonitorTab
    {
        get
        {
            return _showCalibrationMonitorTab;
        }
        set
        {
            SetProperty(ref _showCalibrationMonitorTab, value);
        }
    }

    public int SelectedTabIndex { get
    {
        return _selectedTabIndex;
    } set
    {
        SetProperty(ref _selectedTabIndex, value);
    } }
}
