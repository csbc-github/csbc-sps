using System.Collections;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LMX;

namespace ThreeDScan.Controls;

/// <summary>
/// 進料區掃描視覺化 UserControl（依介面草稿 01_進料區_light.jpg）。
/// 把 <see cref="LMX.LidarFrame"/> 渲染成 X-Z 高度剖面折線圖。
/// 自動依資料範圍縮放、Y 值以中位數為基線顯示成相對 Z（mm）。
/// </summary>
public partial class LidarScanView : UserControl
{
    public LidarScanView()
    {
        InitializeComponent();
        ApplyCalibrationToRenderer();
    }

    public static readonly DependencyProperty FrameProperty = DependencyProperty.Register(
        nameof(Frame), typeof(LidarFrame), typeof(LidarScanView),
        new PropertyMetadata(null, OnFrameChanged));

    public LidarFrame? Frame
    {
        get
        {
            return (LidarFrame?)GetValue(FrameProperty);
        }
        set
        {
            SetValue(FrameProperty, value);
        }
    }

    public static readonly DependencyProperty MaterialsProperty = DependencyProperty.Register(
        nameof(Materials), typeof(IEnumerable), typeof(LidarScanView),
        new PropertyMetadata(null, OnMaterialsChanged));

    public IEnumerable? Materials
    {
        get
        {
            return (IEnumerable?)GetValue(MaterialsProperty);
        }
        set
        {
            SetValue(MaterialsProperty, value);
        }
    }

    public static readonly DependencyProperty AngleMinTextProperty = DependencyProperty.Register(
        nameof(AngleMinText), typeof(string), typeof(LidarScanView), new PropertyMetadata("", OnCalibrationTextChanged));
    public static readonly DependencyProperty AngleMaxTextProperty = DependencyProperty.Register(
        nameof(AngleMaxText), typeof(string), typeof(LidarScanView), new PropertyMetadata("", OnCalibrationTextChanged));
    public static readonly DependencyProperty AngleCorrectionDegTextProperty = DependencyProperty.Register(
        nameof(AngleCorrectionDegText), typeof(string), typeof(LidarScanView), new PropertyMetadata("0", OnCalibrationTextChanged));
    public static readonly DependencyProperty ReferenceYMmTextProperty = DependencyProperty.Register(
        nameof(ReferenceYMmText), typeof(string), typeof(LidarScanView), new PropertyMetadata("", OnCalibrationTextChanged));
    public static readonly DependencyProperty XOffsetMmTextProperty = DependencyProperty.Register(
        nameof(XOffsetMmText), typeof(string), typeof(LidarScanView), new PropertyMetadata("0", OnCalibrationTextChanged));
    public static readonly DependencyProperty ZOffsetMmTextProperty = DependencyProperty.Register(
        nameof(ZOffsetMmText), typeof(string), typeof(LidarScanView), new PropertyMetadata("0", OnCalibrationTextChanged));

    public string AngleMinText { get
    {
        return (string)GetValue(AngleMinTextProperty);
    } set
    {
        SetValue(AngleMinTextProperty, value);
    } }
    public string AngleMaxText { get
    {
        return (string)GetValue(AngleMaxTextProperty);
    } set
    {
        SetValue(AngleMaxTextProperty, value);
    } }
    public string AngleCorrectionDegText { get
    {
        return (string)GetValue(AngleCorrectionDegTextProperty);
    } set
    {
        SetValue(AngleCorrectionDegTextProperty, value);
    } }
    public string ReferenceYMmText { get
    {
        return (string)GetValue(ReferenceYMmTextProperty);
    } set
    {
        SetValue(ReferenceYMmTextProperty, value);
    } }
    public string XOffsetMmText { get
    {
        return (string)GetValue(XOffsetMmTextProperty);
    } set
    {
        SetValue(XOffsetMmTextProperty, value);
    } }
    public string ZOffsetMmText { get
    {
        return (string)GetValue(ZOffsetMmTextProperty);
    } set
    {
        SetValue(ZOffsetMmTextProperty, value);
    } }

    private static void OnCalibrationTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is LidarScanView self)
            self.ApplyCalibrationToRenderer();
    }

    private void ApplyCalibrationToRenderer()
    {
        Renderer.ConfigureCalibration(
            ParseOptionalDouble(AngleMinText),
            ParseOptionalDouble(AngleMaxText),
            ParseDoubleLoose(AngleCorrectionDegText, 0),
            ParseDoubleLoose(XOffsetMmText, 0),
            ParseDoubleLoose(ZOffsetMmText, 0),
            ParseOptionalDouble(ReferenceYMmText));
        Renderer.InvalidateVisual();
    }

    private static double? ParseOptionalDouble(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var t = s.Trim();
        if (double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) return v;
        if (double.TryParse(t, NumberStyles.Float, CultureInfo.CurrentCulture, out v)) return v;
        return null;
    }

    private static double ParseDoubleLoose(string? s, double defaultValue)
    {
        if (string.IsNullOrWhiteSpace(s)) return defaultValue;
        var t = s.Trim();
        if (double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) return v;
        if (double.TryParse(t, NumberStyles.Float, CultureInfo.CurrentCulture, out v)) return v;
        return defaultValue;
    }

    private static void OnFrameChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is LidarScanView self)
        {
            self.EmptyHint.Visibility = e.NewValue is null ? Visibility.Visible : Visibility.Collapsed;
            self.Renderer.Frame = e.NewValue as LidarFrame;
            self.ApplyCalibrationToRenderer();
        }
    }

    private static void OnMaterialsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is LidarScanView self)
            self.Renderer.Materials = e.NewValue as IEnumerable;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var p = e.GetPosition(Renderer);
        var data = Renderer.ScreenToData(p);
        if (data is { } d)
            ReadoutText.Text = $"X: {d.X,7:0.##} mm    Z: {d.Z,6:+0.##;-0.##;0}  mm";
        else
            ReadoutText.Text = "X: —   Z: —   滑鼠移入顯示座標";
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        ReadoutText.Text = "X: —   Z: —   滑鼠移入顯示座標";
    }
}
