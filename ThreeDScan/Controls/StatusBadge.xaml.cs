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
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public static readonly DependencyProperty BadgeTypeProperty = DependencyProperty.Register(
        nameof(BadgeType), typeof(BadgeType), typeof(StatusBadge),
        new PropertyMetadata(BadgeType.Info, OnBadgeTypeChanged));

    public BadgeType BadgeType
    {
        get => (BadgeType)GetValue(BadgeTypeProperty);
        set => SetValue(BadgeTypeProperty, value);
    }

    private static void OnBadgeTypeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((StatusBadge)d).UpdateColor();

    private void UpdateColor()
    {
        var (bgKey, fgKey) = BadgeType switch
        {
            BadgeType.Success => ("SuccessBgBrush", "SuccessTextBrush"),
            BadgeType.Warning => ("WarningBgBrush", "WarningTextBrush"),
            BadgeType.Error => ("ErrorBgBrush", "ErrorTextBrush"),
            _ => ("InfoBgBrush", "InfoTextBrush")
        };
        if (Application.Current?.Resources[bgKey] is Brush bg) PART_Bd.Background = bg;
        if (Application.Current?.Resources[fgKey] is Brush fg) PART_Text.Foreground = fg;
    }
}
