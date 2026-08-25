using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Forms.Integration;
using Keyence.LjDevCommon;
using Keyence.LjDev3dView;
using Keyence.LjDevMeasure;
using ThreeDScan.Infrastructure.Measure;
using ThreeDScan.Infrastructure.Measure.Models;
using WpfMessageBox = System.Windows.MessageBox;

namespace ThreeDScan.Lj3DViewHost;

/// <summary>對齊 LJScanWinform Form1.UpdateViewport + Lj3DView。</summary>
public partial class Lj3DViewportHost : System.Windows.Controls.UserControl
{
    private Lj3DView? _viewport;
    private bool _initialized;

    public Lj3DViewportHost()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_initialized) return;
        _initialized = true;

        try
        {
            _viewport = new Lj3DView();
            var elementHost = new ElementHost
            {
                Dock = DockStyle.Fill,
                Child = _viewport,
            };
            FormsHost.Child = elementHost;
        }
        catch (Exception ex)
        {
            WpfMessageBox.Show(
                ex.ToString(),
                "Lj3DView 初始化失敗",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    public void ShowMeasureResult(LjViewportMeasureResult result)
    {
        if (_viewport is null) return;

        ShowCaptureImages(result.Height, result.Gray);
        _viewport.SetToolInfo(result.ToolResults);
    }

    /// <summary>對齊 LJScanWinform Form1.UpdateViewport（僅顯示高度／灰階，不含量測工具）。</summary>
    public void ShowCaptureFromFiles(string heightImagePath, string grayImagePath)
    {
        if (_viewport is null) return;

        var height = new LHeightImage();
        height.Read(heightImagePath);
        var gray = new LGrayImage();
        gray.Read(grayImagePath);
        ShowCaptureImages(height, gray);
    }

    private void ShowCaptureImages(LHeightImage height, LGrayImage gray)
    {
        if (_viewport is null) return;

        _viewport.ClearImage();
        _viewport.SetImage(height, gray);
        _viewport.ColorRangeFitCommand.Execute();
        _viewport.DisplayBillboard = true;
        _viewport.DisplayArea = true;
    }
}
