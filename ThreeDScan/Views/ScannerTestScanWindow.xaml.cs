using System.Windows;
using ThreeDScan.ViewModels;

namespace ThreeDScan.Views;

public partial class ScannerTestScanWindow : Window
{
    public ScannerTestScanWindow()
    {
        InitializeComponent();
        Activated += OnActivated;
        DataContextChanged += OnDataContextChanged;
    }

    private void OnActivated(object? sender, EventArgs e)
    {
        if (DataContext is ScannerTestScanViewModel vm)
            vm.RefreshScanTargetLabel();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is ScannerTestScanViewModel oldVm)
            oldVm.CapturePreviewReady -= OnCapturePreviewReady;

        if (e.NewValue is ScannerTestScanViewModel newVm)
            newVm.CapturePreviewReady += OnCapturePreviewReady;
    }

    private void OnCapturePreviewReady(object? sender, ScannerCapturePreviewEventArgs e)
    {
        try
        {
            Lj3DHost.ShowCaptureFromFiles(e.HeightImagePath, e.GrayImagePath);
            ViewportPlaceholder.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            ViewportPlaceholder.Visibility = Visibility.Visible;
            MessageBox.Show(ex.Message, "3D 預覽", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
