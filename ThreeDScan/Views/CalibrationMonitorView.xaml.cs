using System.Windows.Controls;
using ThreeDScan.ViewModels;

namespace ThreeDScan.Views;

public partial class CalibrationMonitorView : UserControl
{
    public CalibrationMonitorView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, System.Windows.DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is CalibrationMonitorViewModel oldVm)
            oldVm.ViewportUpdated -= OnViewportUpdated;

        if (e.NewValue is CalibrationMonitorViewModel newVm)
            newVm.ViewportUpdated += OnViewportUpdated;
    }

    private void OnViewportUpdated(object? sender, CalibrationMonitorViewModel.LjViewportUpdateEventArgs e)
    {
        Lj3DHost.ShowMeasureResult(e.Result);
    }
}
