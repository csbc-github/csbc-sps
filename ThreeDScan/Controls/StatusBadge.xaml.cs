using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ThreeDScan.Controls;

public enum BadgeType { Success, Warning, Error, Info }

public partial class StatusBadge : UserControl
{
    public StatusBadge()
    {
        InitializeComponent();
        UpdateColor();
    }

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(StatusBadge), new PropertyMetadata(string.Empty));

    public string Text
    {
        get
        {
            return (string)GetValue(TextProperty);
        }
        set
        {
            SetValue(TextProperty, value);
        }
    }

    public static readonly DependencyProperty BadgeTypeProperty = DependencyProperty.Register(
        nameof(BadgeType), typeof(BadgeType), typeof(StatusBadge),
        new PropertyMetadata(BadgeType.Info, OnBadgeTypeChanged));

    public BadgeType BadgeType
    {
        get
        {
            return (BadgeType)GetValue(BadgeTypeProperty);
        }
        set
        {
            SetValue(BadgeTypeProperty, value);
        }
    }

    private static void OnBadgeTypeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((StatusBadge)d).UpdateColor();
    }

    private void UpdateColor()
    {
        string bgKey;
        string fgKey;
        switch (BadgeType)
        {
            case BadgeType.Success:
                bgKey = "SuccessBgBrush";
                fgKey = "SuccessTextBrush";
                break;
            case BadgeType.Warning:
                bgKey = "WarningBgBrush";
                fgKey = "WarningTextBrush";
                break;
            case BadgeType.Error:
                bgKey = "ErrorBgBrush";
                fgKey = "ErrorTextBrush";
                break;
            default:
                bgKey = "InfoBgBrush";
                fgKey = "InfoTextBrush";
                break;
        }
        if (Application.Current?.Resources[bgKey] is Brush bg) PART_Bd.Background = bg;
        if (Application.Current?.Resources[fgKey] is Brush fg) PART_Text.Foreground = fg;
    }
}
