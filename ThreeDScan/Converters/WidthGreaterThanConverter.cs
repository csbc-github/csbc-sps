using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace ThreeDScan.Converters;

// 視窗寬度 >= parameter 時顯示；否則折疊。
// 用於標題列、副標隨寬度自動收起，配合 AdaptiveSplitView 實作整體自適應。
public sealed class WidthGreaterThanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not double width) return Visibility.Visible;
        double threshold = 0;
        if (parameter is string s) double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out threshold);
        else if (parameter is double dp) threshold = dp;
        return width >= threshold ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}
