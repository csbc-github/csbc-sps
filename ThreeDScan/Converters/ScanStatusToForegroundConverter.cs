using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using ThreeDScan.Models;

namespace ThreeDScan.Converters;

public sealed class ScanStatusToForegroundConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        string key = "InfoTextBrush";
        if (value is ScanStatus s)
        {
            switch (s)
            {
                case ScanStatus.Completed:
                    key = "SuccessTextBrush";
                    break;
                case ScanStatus.Scanning:
                    key = "WarningTextBrush";
                    break;
                case ScanStatus.Error:
                    key = "ErrorTextBrush";
                    break;
            }
        }

        return Application.Current.Resources[key] as Brush ?? Brushes.Black;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return Binding.DoNothing;
    }
}
