using System.Windows;
using System.Windows.Controls;
using ThreeDScan.Models;

namespace ThreeDScan.Controls;

/// <summary>各產線區預覽區上方共用的連線狀態列（綠／紅燈 + 說明）。</summary>
public partial class AreaConnectionStatusBar : UserControl
{
    /// <summary>連線狀態燈（Normal＝綠，Error＝紅）。</summary>
    public static readonly DependencyProperty ConnectionLampProperty =
        DependencyProperty.Register(
            nameof(ConnectionLamp),
            typeof(SystemStatus),
            typeof(AreaConnectionStatusBar),
            new PropertyMetadata(SystemStatus.Error));

    /// <summary>連線狀態說明。</summary>
    public static readonly DependencyProperty ConnectionStatusTextProperty =
        DependencyProperty.Register(
            nameof(ConnectionStatusText),
            typeof(string),
            typeof(AreaConnectionStatusBar),
            new PropertyMetadata("正在檢查連線…"));

    /// <summary>狀態列下方可選的附加內容（例如 PLY 路徑）。</summary>
    public static readonly DependencyProperty DetailContentProperty =
        DependencyProperty.Register(
            nameof(DetailContent),
            typeof(object),
            typeof(AreaConnectionStatusBar),
            new PropertyMetadata(null));

    /// <summary>建立連線狀態列控制項。</summary>
    public AreaConnectionStatusBar()
    {
        InitializeComponent();
    }

    /// <summary>連線狀態燈（Normal＝綠，Error＝紅）。</summary>
    public SystemStatus ConnectionLamp
    {
        get
        {
            return (SystemStatus)GetValue(ConnectionLampProperty);
        }
        set
        {
            SetValue(ConnectionLampProperty, value);
        }
    }

    /// <summary>連線狀態說明。</summary>
    public string ConnectionStatusText
    {
        get
        {
            return (string)GetValue(ConnectionStatusTextProperty);
        }
        set
        {
            SetValue(ConnectionStatusTextProperty, value);
        }
    }

    /// <summary>狀態列下方可選的附加內容。</summary>
    public object? DetailContent
    {
        get
        {
            return GetValue(DetailContentProperty);
        }
        set
        {
            SetValue(DetailContentProperty, value);
        }
    }
}
