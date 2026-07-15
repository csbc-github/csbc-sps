using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ThreeDScan.Controls;

public partial class PreviewPlaceholder : UserControl
{
    public PreviewPlaceholder() { InitializeComponent(); }

    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(PreviewPlaceholder), new PropertyMetadata(string.Empty));
    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }

    public static readonly DependencyProperty SubTitleProperty = DependencyProperty.Register(
        nameof(SubTitle), typeof(string), typeof(PreviewPlaceholder), new PropertyMetadata(string.Empty));
    public string SubTitle { get => (string)GetValue(SubTitleProperty); set => SetValue(SubTitleProperty, value); }

    public static readonly DependencyProperty ImageSourceProperty = DependencyProperty.Register(
        nameof(ImageSource), typeof(ImageSource), typeof(PreviewPlaceholder),
        new PropertyMetadata(null, OnImageSourceChanged));

    public ImageSource? ImageSource
    {
        get => (ImageSource?)GetValue(ImageSourceProperty);
        set => SetValue(ImageSourceProperty, value);
    }

    public static readonly DependencyProperty PlaceholderVisibilityProperty = DependencyProperty.Register(
        nameof(PlaceholderVisibility), typeof(Visibility), typeof(PreviewPlaceholder),
        new PropertyMetadata(Visibility.Visible));

    public Visibility PlaceholderVisibility
    {
        get => (Visibility)GetValue(PlaceholderVisibilityProperty);
        set => SetValue(PlaceholderVisibilityProperty, value);
    }

    private static void OnImageSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is PreviewPlaceholder self)
            self.PlaceholderVisibility = e.NewValue is null ? Visibility.Visible : Visibility.Collapsed;
    }
}
