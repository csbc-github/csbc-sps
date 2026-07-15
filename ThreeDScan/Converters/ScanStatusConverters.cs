using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using ThreeDScan.Models;

namespace ThreeDScan.Converters;

// ScanStatus → 顯示文字
public sealed class ScanStatusToTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is ScanStatus s ? s switch
        {
            ScanStatus.Waiting => "等待掃描",
            ScanStatus.Scanning => "掃描中",
            ScanStatus.Completed => "掃描完成",
            ScanStatus.Error => "掃描錯誤",
            _ => string.Empty
        } : string.Empty;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}

// ScanStatus → 背景 Brush（依 SD §10.1 綠/黃/紅）
public sealed class ScanStatusToBackgroundConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var key = value is ScanStatus s ? s switch
        {
            ScanStatus.Completed => "SuccessBgBrush",
            ScanStatus.Scanning => "WarningBgBrush",
            ScanStatus.Error => "ErrorBgBrush",
            _ => "InfoBgBrush"
        } : "InfoBgBrush";
        return Application.Current.Resources[key] as Brush ?? Brushes.Transparent;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}

// ScanStatus → 文字 Brush
public sealed class ScanStatusToForegroundConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var key = value is ScanStatus s ? s switch
        {
            ScanStatus.Completed => "SuccessTextBrush",
            ScanStatus.Scanning => "WarningTextBrush",
            ScanStatus.Error => "ErrorTextBrush",
            _ => "InfoTextBrush"
        } : "InfoTextBrush";
        return Application.Current.Resources[key] as Brush ?? Brushes.Black;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}

// SystemStatus → 顯示文字
public sealed class SystemStatusToTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is SystemStatus s ? s switch
        {
            SystemStatus.Normal => "運行中",
            SystemStatus.Warning => "警告",
            SystemStatus.Error => "異常",
            _ => string.Empty
        } : string.Empty;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}

// SystemStatus → 狀態燈顏色（綠/黃/紅）
public sealed class SystemStatusToLampBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var key = value is SystemStatus s ? s switch
        {
            SystemStatus.Normal => "SuccessTextBrush",
            SystemStatus.Warning => "WarningTextBrush",
            SystemStatus.Error => "ErrorTextBrush",
            _ => "InfoTextBrush"
        } : "InfoTextBrush";
        return Application.Current.Resources[key] as Brush ?? Brushes.Gray;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}

// SystemStatus → Pill 徽章背景 Brush
public sealed class SystemStatusToBadgeBgConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var key = value is SystemStatus s ? s switch
        {
            SystemStatus.Normal => "SuccessBgBrush",
            SystemStatus.Warning => "WarningBgBrush",
            SystemStatus.Error => "ErrorBgBrush",
            _ => "InfoBgBrush"
        } : "InfoBgBrush";
        return Application.Current.Resources[key] as Brush ?? Brushes.Transparent;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}

// SystemStatus → Pill 徽章文字 Brush
public sealed class SystemStatusToBadgeFgConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var key = value is SystemStatus s ? s switch
        {
            SystemStatus.Normal => "SuccessTextBrush",
            SystemStatus.Warning => "WarningTextBrush",
            SystemStatus.Error => "ErrorTextBrush",
            _ => "InfoTextBrush"
        } : "InfoTextBrush";
        return Application.Current.Resources[key] as Brush ?? Brushes.Gray;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}
