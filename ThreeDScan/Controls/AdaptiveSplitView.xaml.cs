using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ThreeDScan.Controls;

// 自適應分欄／分列容器：寬>=Threshold 走左右；寬<Threshold 走上下。
// 內含 GridSplitter，允許使用者在兩種版面下各自拖曳調整。
public partial class AdaptiveSplitView : UserControl
{
    public AdaptiveSplitView()
    {
        InitializeComponent();
        SizeChanged += (_, _) => ApplyLayout();
        Loaded += (_, _) => ApplyLayout();
    }

    public static readonly DependencyProperty PrimaryContentProperty = DependencyProperty.Register(
        nameof(PrimaryContent), typeof(object), typeof(AdaptiveSplitView));
    public object? PrimaryContent { get
    {
        return GetValue(PrimaryContentProperty);
    } set
    {
        SetValue(PrimaryContentProperty, value);
    } }

    public static readonly DependencyProperty SecondaryContentProperty = DependencyProperty.Register(
        nameof(SecondaryContent), typeof(object), typeof(AdaptiveSplitView));
    public object? SecondaryContent { get
    {
        return GetValue(SecondaryContentProperty);
    } set
    {
        SetValue(SecondaryContentProperty, value);
    } }

    public static readonly DependencyProperty ThresholdProperty = DependencyProperty.Register(
        nameof(Threshold), typeof(double), typeof(AdaptiveSplitView),
        new PropertyMetadata(820.0, OnLayoutAffectingPropertyChanged));
    public double Threshold { get
    {
        return (double)GetValue(ThresholdProperty);
    } set
    {
        SetValue(ThresholdProperty, value);
    } }

    public static readonly DependencyProperty PrimaryRatioProperty = DependencyProperty.Register(
        nameof(PrimaryRatio), typeof(double), typeof(AdaptiveSplitView),
        new PropertyMetadata(2.0, OnLayoutAffectingPropertyChanged));
    public double PrimaryRatio { get
    {
        return (double)GetValue(PrimaryRatioProperty);
    } set
    {
        SetValue(PrimaryRatioProperty, value);
    } }

    public static readonly DependencyProperty SecondaryRatioProperty = DependencyProperty.Register(
        nameof(SecondaryRatio), typeof(double), typeof(AdaptiveSplitView),
        new PropertyMetadata(1.0, OnLayoutAffectingPropertyChanged));
    public double SecondaryRatio { get
    {
        return (double)GetValue(SecondaryRatioProperty);
    } set
    {
        SetValue(SecondaryRatioProperty, value);
    } }

    public static readonly DependencyProperty PrimaryMinWidthProperty = DependencyProperty.Register(
        nameof(PrimaryMinWidth), typeof(double), typeof(AdaptiveSplitView),
        new PropertyMetadata(480.0, OnLayoutAffectingPropertyChanged));
    public double PrimaryMinWidth { get
    {
        return (double)GetValue(PrimaryMinWidthProperty);
    } set
    {
        SetValue(PrimaryMinWidthProperty, value);
    } }

    public static readonly DependencyProperty SecondaryMinWidthProperty = DependencyProperty.Register(
        nameof(SecondaryMinWidth), typeof(double), typeof(AdaptiveSplitView),
        new PropertyMetadata(300.0, OnLayoutAffectingPropertyChanged));
    public double SecondaryMinWidth { get
    {
        return (double)GetValue(SecondaryMinWidthProperty);
    } set
    {
        SetValue(SecondaryMinWidthProperty, value);
    } }

    public static readonly DependencyProperty PrimaryMinHeightProperty = DependencyProperty.Register(
        nameof(PrimaryMinHeight), typeof(double), typeof(AdaptiveSplitView),
        new PropertyMetadata(260.0, OnLayoutAffectingPropertyChanged));
    public double PrimaryMinHeight { get
    {
        return (double)GetValue(PrimaryMinHeightProperty);
    } set
    {
        SetValue(PrimaryMinHeightProperty, value);
    } }

    public static readonly DependencyProperty SecondaryMinHeightProperty = DependencyProperty.Register(
        nameof(SecondaryMinHeight), typeof(double), typeof(AdaptiveSplitView),
        new PropertyMetadata(200.0, OnLayoutAffectingPropertyChanged));
    public double SecondaryMinHeight { get
    {
        return (double)GetValue(SecondaryMinHeightProperty);
    } set
    {
        SetValue(SecondaryMinHeightProperty, value);
    } }

    public static readonly DependencyProperty SplitterSizeProperty = DependencyProperty.Register(
        nameof(SplitterSize), typeof(double), typeof(AdaptiveSplitView),
        new PropertyMetadata(5.0, OnLayoutAffectingPropertyChanged));
    public double SplitterSize { get
    {
        return (double)GetValue(SplitterSizeProperty);
    } set
    {
        SetValue(SplitterSizeProperty, value);
    } }

    private bool _isVertical;

    private static void OnLayoutAffectingPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        (d as AdaptiveSplitView)?.ApplyLayout();
    }

    private void ApplyLayout()
    {
        if (PART_Grid is null) return;
        var vertical = ActualWidth > 0 && ActualWidth < Threshold;
        _isVertical = vertical;

        PART_Grid.ColumnDefinitions.Clear();
        PART_Grid.RowDefinitions.Clear();

        if (vertical)
        {
            PART_Grid.RowDefinitions.Add(new RowDefinition
            {
                Height = new GridLength(PrimaryRatio, GridUnitType.Star),
                MinHeight = PrimaryMinHeight
            });
            PART_Grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(SplitterSize) });
            PART_Grid.RowDefinitions.Add(new RowDefinition
            {
                Height = new GridLength(SecondaryRatio, GridUnitType.Star),
                MinHeight = SecondaryMinHeight
            });

            Grid.SetRow(PART_Primary, 0); Grid.SetColumn(PART_Primary, 0);
            Grid.SetRow(PART_Splitter, 1); Grid.SetColumn(PART_Splitter, 0);
            Grid.SetRow(PART_Secondary, 2); Grid.SetColumn(PART_Secondary, 0);

            PART_Splitter.ResizeDirection = GridResizeDirection.Rows;
            PART_Splitter.HorizontalAlignment = HorizontalAlignment.Stretch;
            PART_Splitter.VerticalAlignment = VerticalAlignment.Stretch;
            PART_Splitter.Height = double.NaN;
            PART_Splitter.Width = double.NaN;
            PART_Splitter.Cursor = Cursors.SizeNS;
        }
        else
        {
            PART_Grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(PrimaryRatio, GridUnitType.Star),
                MinWidth = PrimaryMinWidth
            });
            PART_Grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(SplitterSize) });
            PART_Grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(SecondaryRatio, GridUnitType.Star),
                MinWidth = SecondaryMinWidth
            });

            Grid.SetColumn(PART_Primary, 0); Grid.SetRow(PART_Primary, 0);
            Grid.SetColumn(PART_Splitter, 1); Grid.SetRow(PART_Splitter, 0);
            Grid.SetColumn(PART_Secondary, 2); Grid.SetRow(PART_Secondary, 0);

            PART_Splitter.ResizeDirection = GridResizeDirection.Columns;
            PART_Splitter.HorizontalAlignment = HorizontalAlignment.Stretch;
            PART_Splitter.VerticalAlignment = VerticalAlignment.Stretch;
            PART_Splitter.Width = double.NaN;
            PART_Splitter.Height = double.NaN;
            PART_Splitter.Cursor = Cursors.SizeWE;
        }
    }
}
