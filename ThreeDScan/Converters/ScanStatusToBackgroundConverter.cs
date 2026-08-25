using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using ThreeDScan.Models;

namespace ThreeDScan.Converters;

public sealed class ScanStatusToBackgroundConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        string key = "InfoBgBrush";
        if (value is ScanStatus s)
        {
            switch (s)
            {
                case ScanStatus.Completed:
                    key = "SuccessBgBrush";
                    break;
                case ScanStatus.Scanning:
                    key = "WarningBgBrush";
                    break;
                case ScanStatus.Error:
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
