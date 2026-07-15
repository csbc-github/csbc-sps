using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using LMX;
using ThreeDScan.Models;

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
        get => (LidarFrame?)GetValue(FrameProperty);
        set => SetValue(FrameProperty, value);
    }

    public static readonly DependencyProperty MaterialsProperty = DependencyProperty.Register(
        nameof(Materials), typeof(IEnumerable), typeof(LidarScanView),
        new PropertyMetadata(null, OnMaterialsChanged));

    public IEnumerable? Materials
    {
        get => (IEnumerable?)GetValue(MaterialsProperty);
        set => SetValue(MaterialsProperty, value);
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

    public string AngleMinText { get => (string)GetValue(AngleMinTextProperty); set => SetValue(AngleMinTextProperty, value); }
    public string AngleMaxText { get => (string)GetValue(AngleMaxTextProperty); set => SetValue(AngleMaxTextProperty, value); }
    public string AngleCorrectionDegText { get => (string)GetValue(AngleCorrectionDegTextProperty); set => SetValue(AngleCorrectionDegTextProperty, value); }
    public string ReferenceYMmText { get => (string)GetValue(ReferenceYMmTextProperty); set => SetValue(ReferenceYMmTextProperty, value); }
    public string XOffsetMmText { get => (string)GetValue(XOffsetMmTextProperty); set => SetValue(XOffsetMmTextProperty, value); }
    public string ZOffsetMmText { get => (string)GetValue(ZOffsetMmTextProperty); set => SetValue(ZOffsetMmTextProperty, value); }

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

/// <summary>
/// X-Z 高度剖面圖渲染器。
/// 自動依當前 frame 的 Cartesian 範圍縮放；以 Y 值中位數為基線（baseline）將 Z 顯示成相對值。
/// 物料分區色塊：依 <see cref="Materials"/> 集合（每筆 <see cref="MaterialItem"/> 的 LeftCorner.X / RightCorner.X）。
/// </summary>
public sealed class LidarScanRenderer : FrameworkElement
{
    private LidarFrame? _frame;
    private IEnumerable? _materials;

    // 繪圖區（去掉軸標籤邊距後的內框）
    private Rect _plotRect;
    // 資料軸範圍（Z 為相對基線；X 為原始 Cartesian.X）
    private double _xMin, _xMax, _zMin, _zMax;
    private double _baselineY;
    private double _absBase;

    private double? _angleMinDeg;
    private double? _angleMaxDeg;
    private double _angleCorrectionDeg;
    private double _xOffsetMm;
    private double _zOffsetMm;
    private double? _referenceYMm;

    /// <summary>由 <see cref="LidarScanView"/> 套用輸入框校正。</summary>
    public void ConfigureCalibration(
        double? angleMinDeg,
        double? angleMaxDeg,
        double angleCorrectionDeg,
        double xOffsetMm,
        double zOffsetMm,
        double? referenceYMm)
    {
        _angleMinDeg = angleMinDeg;
        _angleMaxDeg = angleMaxDeg;
        _angleCorrectionDeg = angleCorrectionDeg;
        _xOffsetMm = xOffsetMm;
        _zOffsetMm = zOffsetMm;
        _referenceYMm = referenceYMm;
    }

    /// <summary>
    /// 依校正後角度重算 X、Y（與 <see cref="LidarFrame"/> 內建公式一致），並套用角度視窗與 X 偏移。
    /// </summary>
    private bool TryPlaneXY(LidarFrame f, int i, out double xDisp, out double yPlane)
    {
        xDisp = 0;
        yPlane = 0;
        if (i < 0 || i >= f.Distances.Length) return false;
        int d = f.Distances[i];
        if (!IsValidDistance(d)) return false;

        double angleDeg = i * f.AngleResolutionDeg - f.StartAngleDeg + _angleCorrectionDeg;
        double? lo = _angleMinDeg;
        double? hi = _angleMaxDeg;
        if (lo is double a && hi is double b && a > b) { lo = b; hi = a; }
        if (lo is double minA && angleDeg < minA) return false;
        if (hi is double maxA && angleDeg > maxA) return false;

        double rad = angleDeg * Math.PI / 180.0;
        xDisp = d * Math.Cos(rad) + _xOffsetMm;
        yPlane = d * Math.Sin(rad);
        return true;
    }

    private const double MarginRight = 8;
    private const double MarginTop = 8;
    private const double MarginBottom = 24;   // X 軸刻度標籤
    private const double MarginLeft = 44;     // Y 軸刻度標籤

    private static readonly Brush PlotBackground = Freeze(new SolidColorBrush(Color.FromRgb(0xFE, 0xFE, 0xFE)));
    private static readonly Pen FramePen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0xCB, 0xD5, 0xE1)), 1));
    private static readonly Pen GridPen = Freeze(new Pen(new SolidColorBrush(Color.FromArgb(110, 0xCB, 0xD5, 0xE1)), 0.6)
    { DashStyle = new DashStyle(new[] { 3.0, 3.0 }, 0) });
    private static readonly Brush LabelBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x4B, 0x55, 0x63)));
    private static readonly Pen ProfilePen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0x9F, 0x12, 0x39)), 1.4));
    private static readonly Brush ProfileDotBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x9F, 0x12, 0x39)));
    private static readonly Brush BaselinePen = Freeze(new SolidColorBrush(Color.FromArgb(180, 0x9C, 0xA3, 0xAF)));
    private static readonly Brush LidarBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x33, 0x77, 0xCC)));
    private static readonly Pen LidarBeamPen = Freeze(new Pen(new SolidColorBrush(Color.FromArgb(80, 0x33, 0x77, 0xCC)), 0.8)
    { DashStyle = new DashStyle(new[] { 2.0, 3.0 }, 0) });
    private static readonly Typeface LabelTypeface = new("Segoe UI");

    /// <summary>距離有效性：0 = 無回波、1 = 雜訊、>= 999000 = 飽和/超量程。</summary>
    private static bool IsValidDistance(int d) => d > 1 && d < 999000;

    /// <summary>物料分區色塊調色盤（半透明）。對應介面草稿的米黃 / 橙 / 綠。</summary>
    private static readonly Brush[] ZoneBrushes =
    {
        Freeze(new SolidColorBrush(Color.FromArgb(50, 0xFB, 0xBF, 0x24))),  // amber
        Freeze(new SolidColorBrush(Color.FromArgb(50, 0xF9, 0x73, 0x16))),  // orange
        Freeze(new SolidColorBrush(Color.FromArgb(50, 0x10, 0xB9, 0x81))),  // emerald
    };

    private static T Freeze<T>(T f) where T : Freezable { f.Freeze(); return f; }

    public LidarFrame? Frame
    {
        get => _frame;
        set { _frame = value; InvalidateVisual(); }
    }

    public IEnumerable? Materials
    {
        get => _materials;
        set { _materials = value; InvalidateVisual(); }
    }

    /// <summary>把螢幕座標換算成資料座標 (X mm, Z mm)。</summary>
    public (double X, double Z)? ScreenToData(Point screen)
    {
        if (_plotRect.Width <= 0 || _plotRect.Height <= 0) return null;
        if (screen.X < _plotRect.Left || screen.X > _plotRect.Right) return null;
        if (screen.Y < _plotRect.Top || screen.Y > _plotRect.Bottom) return null;
        if (_xMax <= _xMin || _zMax <= _zMin) return null;
        double x = _xMin + (screen.X - _plotRect.Left) / _plotRect.Width * (_xMax - _xMin);
        double z = _zMin + (1 - (screen.Y - _plotRect.Top) / _plotRect.Height) * (_zMax - _zMin);
        return (x, z);
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (ActualWidth <= 0 || ActualHeight <= 0) return;

        // 整個元件透明背景，讓 Hit-test 可達
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));

        ComputeLayoutAndRanges();

        // 繪圖區外框
        dc.DrawRectangle(PlotBackground, FramePen, _plotRect);

        DrawMaterialZones(dc);
        DrawGrid(dc);
        DrawBaseline(dc);

        if (_frame is not null && _frame.Cartesian.Length >= 2)
            DrawProfile(dc);

        DrawLidarMarker(dc);
    }

    private void ComputeLayoutAndRanges()
    {
        _plotRect = new Rect(
            MarginLeft, MarginTop,
            Math.Max(0, ActualWidth - MarginLeft - MarginRight),
            Math.Max(0, ActualHeight - MarginTop - MarginBottom));

        if (_frame is null || _frame.Cartesian.Length == 0)
        {
            // 無資料時：給 readout 一個合理的預設範圍（Z 以輸送帶面為 0、向上為正）
            _xMin = -500; _xMax = 500;
            _zMin = 0; _zMax = 50;
            _baselineY = 0;
            _absBase = 0;
            return;
        }

        var f = _frame;
        int n = f.Distances.Length;

        var ys = new List<double>(n);
        double xmin = double.MaxValue, xmax = double.MinValue;
        for (int i = 0; i < n; i++)
        {
            if (!TryPlaneXY(f, i, out var x, out var y)) continue;
            if (x < xmin) xmin = x;
            if (x > xmax) xmax = x;
            ys.Add(y);
        }

        if (ys.Count == 0)
        {
            _xMin = -500; _xMax = 500;
            _zMin = 0; _zMax = 50;
            _baselineY = 0;
            _absBase = 0;
            return;
        }

        ys.Sort();
        _baselineY = _referenceYMm ?? ys[ys.Count / 2];
        _absBase = Math.Abs(_baselineY);

        double zMin = double.MaxValue, zMax = double.MinValue;
        for (int i = 0; i < n; i++)
        {
            if (!TryPlaneXY(f, i, out _, out var y)) continue;
            double z = _absBase - Math.Abs(y) + _zOffsetMm;
            if (z < zMin) zMin = z;
            if (z > zMax) zMax = z;
        }

        // X 範圍：使用有效資料極值，加 5% padding
        double xPad = (xmax - xmin) * 0.05;
        _xMin = xmin - xPad;
        _xMax = xmax + xPad;

        // Z 範圍：Z=0 為「參考面」（中位數 |Y| 對應的距離），物體頂端為正。
        // 先前誤用 Math.Min(zMin, -10)，在「全部 Z≥0」時仍強制顯示負刻度 — 已改為僅在資料真有負值時才延伸到負向。
        double span = Math.Max(zMax - zMin, 1e-3);
        double padUp = Math.Max(span * 0.12, 8);
        if (zMin >= 0)
        {
            // 沒有比參考面更遠的點：軸從 0 起，不畫負刻度（與「輸送帶在底部」一致）。
            _zMin = 0;
            _zMax = Math.Max(zMax + padUp, 50);
        }
        else
        {
            double padDown = Math.Max(Math.Abs(zMin) * 0.12, 4);
            _zMin = zMin - padDown;
            _zMax = zMax + padUp;
        }
    }

    private Point DataToScreen(double x, double z)
    {
        double sx = _plotRect.Left + (x - _xMin) / (_xMax - _xMin) * _plotRect.Width;
        double sy = _plotRect.Top + _plotRect.Height - (z - _zMin) / (_zMax - _zMin) * _plotRect.Height;
        return new Point(sx, sy);
    }

    private void DrawGrid(DrawingContext dc)
    {
        if (_plotRect.Width <= 0 || _plotRect.Height <= 0) return;

        double xStep = NiceStep((_xMax - _xMin) / 8);
        double zStep = NiceStep((_zMax - _zMin) / 6);
        if (xStep <= 0 || zStep <= 0) return;

        // X：垂直格線 + 底部標籤
        double xStart = Math.Ceiling(_xMin / xStep) * xStep;
        for (double x = xStart; x <= _xMax + 1e-6; x += xStep)
        {
            var top = DataToScreen(x, _zMax);
            var bottom = DataToScreen(x, _zMin);
            dc.DrawLine(GridPen, new Point(top.X, _plotRect.Top), new Point(top.X, _plotRect.Bottom));
            var t = MakeLabel(FormatTick(x, xStep));
            dc.DrawText(t, new Point(bottom.X - t.Width / 2, _plotRect.Bottom + 3));
        }

        // Z：水平格線 + 左側標籤
        double zStart = Math.Ceiling(_zMin / zStep) * zStep;
        for (double z = zStart; z <= _zMax + 1e-6; z += zStep)
        {
            var p = DataToScreen(_xMin, z);
            dc.DrawLine(GridPen, new Point(_plotRect.Left, p.Y), new Point(_plotRect.Right, p.Y));
            var t = MakeLabel(FormatTick(z, zStep, signed: true));
            dc.DrawText(t, new Point(_plotRect.Left - t.Width - 4, p.Y - t.Height / 2));
        }
    }

    /// <summary>畫水平基線（Z=0），讓使用者能對照「凸起 vs. 平坦」。</summary>
    private void DrawBaseline(DrawingContext dc)
    {
        // 僅在 Z=0 落在可視資料範圍內時畫參考線（輸送帶 / 參考面）
        if (0 < _zMin || 0 > _zMax) return;
        var p = DataToScreen(_xMin, 0);
        var pen = new Pen(BaselinePen, 1.0);
        pen.Freeze();
        dc.DrawLine(pen, new Point(_plotRect.Left, p.Y), new Point(_plotRect.Right, p.Y));
    }

    private void DrawProfile(DrawingContext dc)
    {
        var f = _frame!;
        int n = f.Distances.Length;

        // 把連續的有效點切成一段段線段；遇到無效點就「斷線」，避免畫面從 0,0 拉一條長線過去。
        var segments = new List<List<Point>>();
        var current = new List<Point>(n);
        var allDots = new List<Point>(n);
        for (int i = 0; i < n; i++)
        {
            if (!TryPlaneXY(f, i, out var x, out var y))
            {
                if (current.Count >= 2) segments.Add(current);
                current = new List<Point>();
                continue;
            }
            double z = _absBase - Math.Abs(y) + _zOffsetMm;
            var p = DataToScreen(x, z);
            current.Add(p);
            allDots.Add(p);
        }
        if (current.Count >= 2) segments.Add(current);

        if (segments.Count == 0) return;

        var geo = new StreamGeometry();
        using (var c = geo.Open())
        {
            foreach (var seg in segments)
            {
                c.BeginFigure(seg[0], false, false);
                if (seg.Count > 1)
                    c.PolyLineTo(seg.GetRange(1, seg.Count - 1), true, false);
            }
        }
        geo.Freeze();

        dc.PushClip(new RectangleGeometry(_plotRect));
        dc.DrawGeometry(null, ProfilePen, geo);

        // 點數不太多時加散點，方便看資料密度
        if (allDots.Count <= 1200)
        {
            foreach (var p in allDots)
            {
                if (p.X < _plotRect.Left - 2 || p.X > _plotRect.Right + 2) continue;
                if (p.Y < _plotRect.Top - 2 || p.Y > _plotRect.Bottom + 2) continue;
                dc.DrawEllipse(ProfileDotBrush, null, p, 1.0, 1.0);
            }
        }
        dc.Pop();
    }

    /// <summary>
    /// 在繪圖區頂端中央畫一個簡單的雷達圖示與兩條光束虛線，
    /// 視覺強調「雷達在上面、由上往下掃」。
    /// </summary>
    private void DrawLidarMarker(DrawingContext dc)
    {
        if (_plotRect.Width <= 40 || _plotRect.Height <= 40) return;
        double cx = _plotRect.Left + _plotRect.Width * 0.5;
        double cy = _plotRect.Top + 6;

        // 兩條斜下的虛線光束，象徵掃描扇面
        double beamLen = Math.Min(_plotRect.Width, _plotRect.Height) * 0.55;
        dc.DrawLine(LidarBeamPen,
            new Point(cx, cy + 4),
            new Point(cx - beamLen * 0.6, cy + beamLen));
        dc.DrawLine(LidarBeamPen,
            new Point(cx, cy + 4),
            new Point(cx + beamLen * 0.6, cy + beamLen));

        // 倒三角作為雷達本體
        var tri = new StreamGeometry();
        using (var c = tri.Open())
        {
            c.BeginFigure(new Point(cx - 6, cy), true, true);
            c.LineTo(new Point(cx + 6, cy), true, false);
            c.LineTo(new Point(cx, cy + 8), true, false);
        }
        tri.Freeze();
        dc.DrawGeometry(LidarBrush, null, tri);

        var label = MakeLabel("雷達");
        dc.DrawText(label, new Point(cx + 9, cy - 1));
    }

    private void DrawMaterialZones(DrawingContext dc)
    {
        if (_materials is null) return;
        int idx = 0;
        foreach (var item in _materials)
        {
            if (item is not MaterialItem mi) { idx++; continue; }
            double left = mi.LeftCorner.X + _xOffsetMm;
            double right = mi.RightCorner.X + _xOffsetMm;
            if (right <= left) { idx++; continue; }

            // 範圍若超出資料軸，clip 到繪圖區
            double l = Math.Max(left, _xMin);
            double r = Math.Min(right, _xMax);
            if (r <= l) { idx++; continue; }

            var topLeft = DataToScreen(l, _zMax);
            var bottomRight = DataToScreen(r, _zMin);
            var rect = new Rect(topLeft, bottomRight);
            rect.Intersect(_plotRect);
            if (rect.Width <= 0 || rect.Height <= 0) { idx++; continue; }

            var brush = ZoneBrushes[idx % ZoneBrushes.Length];
            dc.DrawRectangle(brush, null, rect);
            idx++;
        }
    }

    private FormattedText MakeLabel(string text) =>
        new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            LabelTypeface, 11, LabelBrush, 1.0);

    /// <summary>對齊到「漂亮」的刻度間距，例如 1, 2, 5, 10, 20, 50…</summary>
    private static double NiceStep(double rough)
    {
        if (rough <= 0 || double.IsInfinity(rough) || double.IsNaN(rough)) return 1;
        double exp = Math.Pow(10, Math.Floor(Math.Log10(rough)));
        double frac = rough / exp;
        double nice = frac < 1.5 ? 1 : (frac < 3 ? 2 : (frac < 7 ? 5 : 10));
        return nice * exp;
    }

    private static string FormatTick(double v, double step, bool signed = false)
    {
        // 根據 step 大小自動決定小數位
        int decimals = step >= 1 ? 0 : (step >= 0.1 ? 1 : 2);
        string fmt = "F" + decimals;
        if (signed && v > 0) return "+" + v.ToString(fmt, CultureInfo.InvariantCulture);
        if (signed && v == 0) return v.ToString(fmt, CultureInfo.InvariantCulture);
        return v.ToString(fmt, CultureInfo.InvariantCulture);
    }
}
