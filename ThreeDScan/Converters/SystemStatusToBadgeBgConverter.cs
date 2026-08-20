using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using ThreeDScan.Models;

namespace ThreeDScan.Converters;

public sealed class SystemStatusToBadgeBgConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        string key = "InfoBgBrush";
        if (value is SystemStatus s)
        {
            switch (s)
            {
                case SystemStatus.Normal:
                    key = "SuccessBgBrush";
                    break;
                case SystemStatus.Warning:
                    key = "WarningBgBrush";
                    break;
                case SystemStatus.Error:
                    key = "ErrorBgBrush";
                    break;
            }
        }

        return Application.Current.Resources[key] as Brush ?? Brushes.Transparent;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return Binding.DoNothing;
    }
}
