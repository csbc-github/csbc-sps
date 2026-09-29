using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace ThreeDScan.Converters;

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var b = value is bool v && v;
        if (Invert)
        {
            b = !b;
        }

        if (b)
        {
            return Visibility.Visible;
        }

        var useHidden = parameter is string s
            && s.Equals("Hidden", StringComparison.OrdinalIgnoreCase);
        if (useHidden)
        {
            return Visibility.Hidden;
        }

        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var v = value is Visibility.Visible;
        return Invert ? !v : v;
    }
}
