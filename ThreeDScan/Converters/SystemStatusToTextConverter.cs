using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using ThreeDScan.Models;

namespace ThreeDScan.Converters;

public sealed class SystemStatusToTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not SystemStatus s)
            return string.Empty;

        switch (s)
        {
            case SystemStatus.Normal:
                return "運行中";
            case SystemStatus.Warning:
                return "警告";
            case SystemStatus.Error:
                return "異常";
            default:
                return string.Empty;
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return Binding.DoNothing;
    }
}
