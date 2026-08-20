using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using ThreeDScan.Models;

namespace ThreeDScan.Converters;

public sealed class ScanStatusToTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not ScanStatus s)
            return string.Empty;

        switch (s)
        {
            case ScanStatus.Waiting:
                return "等待掃描";
            case ScanStatus.Scanning:
                return "掃描中";
            case ScanStatus.Completed:
                return "掃描完成";
            case ScanStatus.Error:
                return "掃描錯誤";
            default:
                return string.Empty;
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return Binding.DoNothing;
    }
}
