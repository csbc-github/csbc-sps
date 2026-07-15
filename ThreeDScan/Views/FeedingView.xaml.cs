using System.Windows;
using System.Windows.Controls;
using ThreeDScan.ViewModels;

namespace ThreeDScan.Views;

public partial class FeedingView : UserControl
{
    private FrameInspectorWindow? _inspector;

    public FeedingView() { InitializeComponent(); }

    private void OnOpenInspector(object sender, RoutedEventArgs e)
    {
        if (DataContext is not FeedingViewModel vm) return;

        // 已開啟就帶到前景，避免使用者連點開出多個視窗。
        if (_inspector != null)
        {
            _inspector.Activate();
            return;
        }

        _inspector = new FrameInspectorWindow(vm)
        {
            Owner = Window.GetWindow(this),
        };
        _inspector.Closed += (_, __) => _inspector = null;
        _inspector.Show();
    }
}
