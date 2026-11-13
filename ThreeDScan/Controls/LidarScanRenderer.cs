using System.Collections;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using LMX;
using ThreeDScan.Domain.Models;
using ThreeDScan.Models;

namespace ThreeDScan.Controls;

/// <summary>
/// X-Z 高度剖面圖渲染器。
/// 自動依當前 frame 的 Cartesian 範圍縮放；以 Y 值中位數為基線（baseline）將 Z 顯示成相對值。
/// </summary>
public sealed class LidarScanRenderer : FrameworkElement
{
    private LidarFrame? _frame;
    private IEnumerable? _materials;
    private IEnumerable? _profileSnapPoints;

    // 繪圖區（去掉軸標籤邊距後的內框）
    private Rect _plotRect;
    // 資料軸範圍（Z 為相對基線；X 為原始 Cartesian.X）
    private double _xMin, _xMax, _zMin, _zMax;
    private double _baselineY;
    private double _absBase;

    /// <summary>目前剖面基線 Y（scanner mm），供點選貼齊。</summary>
    public double BaselineY
    {
        get
        {
            return _baselineY;
        }
    }

    private double? _angleMinDeg;
    private double? _angleMaxDeg;
    private double _angleCorrectionDeg;
    private double _xOffsetMm;
    private double _zOffsetMm;
    private double? _referenceYMm;
    private LineScanRegion? _region;
    private bool _useFixedPlotRange;
    private double _fixedXMin;
    private double _fixedXMax;
    private double _fixedZMin;
    private double _fixedZMax;

    /// <summary>框選範圍（掃描器平面）。只畫框線，剖面仍全部顯示。</summary>
    public LineScanRegion? SensorRegion
    {
        get
        {
            return _region;
        }
        set
        {
            _region = value;
            InvalidateVisual();
        }
    }

    /// <summary>桌面校正第一點 X（scanner mm）。</summary>
    public double? DesktopFirstX { get; set; }

    /// <summary>桌面校正第一點 Y。</summary>
    public double? DesktopFirstY { get; set; }

    /// <summary>桌面校正第二點 X。</summary>
    public double? DesktopSecondX { get; set; }

    /// <summary>桌面校正第二點 Y。</summary>
    public double? DesktopSecondY { get; set; }

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

    /// <summary>設定固定 X／Z 顯示範圍；啟用時座標軸不再隨每圈資料極值跳動。</summary>
    /// <param name="useFixed">true 時採用固定範圍。</param>
    /// <param name="xMin">X 下限（mm）。</param>
    /// <param name="xMax">X 上限（mm）。</param>
    /// <param name="zMin">Z 下限（mm）。</param>
    /// <param name="zMax">Z 上限（mm）。</param>
    public void ConfigureFixedPlotRange(bool useFixed, double xMin, double xMax, double zMin, double zMax)
    {
        _useFixedPlotRange = useFixed
            && double.IsFinite(xMin)
            && double.IsFinite(xMax)
            && double.IsFinite(zMin)
            && double.IsFinite(zMax)
            && xMin < xMax
            && zMin < zMax;
        _fixedXMin = xMin;
        _fixedXMax = xMax;
        _fixedZMin = zMin;
        _fixedZMax = zMax;
        InvalidateVisual();
    }

    /// <summary>在凍結輪廓上依螢幕座標貼齊最近點。</summary>
    public ScannerPlanePoint? PickNearestSnapPoint(Point screen, IEnumerable? snaps)
    {
        if (snaps is null)
        {
            return null;
        }

        ComputeLayoutAndRanges();
        if (_plotRect.Width <= 0 || _plotRect.Height <= 0)
        {
            return null;
        }

        ScannerPlanePoint? best = null;
        double bestDist = double.MaxValue;
        foreach (var item in snaps)
        {
            if (item is not ScannerPlanePoint pt)
            {
                continue;
            }

            if (!TryNativePlaneXY(pt, out double x, out double y)) continue;
            double z = _absBase - Math.Abs(y) + _zOffsetMm;
            var disp = DataToScreen(x, z);
            double dx = disp.X - screen.X;
            double dy = disp.Y - screen.Y;
            double dist = dx * dx + dy * dy;
            if (dist < bestDist)
            {
                bestDist = dist;
                best = pt;
            }
        }

        return best;
    }

    /// <summary>把畫面上拖出的矩形換成掃描器平面的框。太小則不採用。</summary>
    public LineScanRegion? TryCreateSensorRegion(Point start, Point end)
    {
        ComputeLayoutAndRanges();
        if (!TrySensorPoint(start, out var x0, out var y0) || !TrySensorPoint(end, out var x1, out var y1))
        {
            return null;
        }

        var xMin = Math.Min(x0, x1);
        var xMax = Math.Max(x0, x1);
        var yMin = Math.Min(y0, y1);
        var yMax = Math.Max(y0, y1);
        if (xMax - xMin < 1 || yMax - yMin < 1)
        {
            return null;
        }

        return new LineScanRegion
        {
            XMin = xMin,
            XMax = xMax,
            YMin = yMin,
            YMax = yMax,
        };
    }

    private bool TrySensorPoint(Point screen, out double sensorX, out double sensorY)
    {
        sensorX = 0;
        sensorY = 0;
        var data = ScreenToData(screen);
        if (data is null)
        {
            return false;
        }

        sensorX = data.Value.X - _xOffsetMm;
        var absoluteY = _absBase - (data.Value.Z - _zOffsetMm);
        var sign = _baselineY < 0 ? -1.0 : 1.0;
        sensorY = sign * absoluteY;
        if (_angleCorrectionDeg != 0)
        {
            double radians = _angleCorrectionDeg * (Math.PI / 180);
            double cosine = Math.Cos(radians), sine = Math.Sin(radians);
            (sensorX, sensorY) = (sensorX * cosine + sensorY * sine, -sensorX * sine + sensorY * cosine);
        }
        return true;
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

    private bool TryNativePlaneXY(ScannerPlanePoint point, out double xDisp, out double yPlane)
    {
        xDisp = 0;
        yPlane = 0;
        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y) || point.X == 0 && point.Y == 0) return false;
        double angle = Math.Atan2(point.Y, point.X) * (180 / Math.PI) + _angleCorrectionDeg;
        double? lo = _angleMinDeg, hi = _angleMaxDeg;
        if (lo is double a && hi is double b && a > b) { lo = b; hi = a; }
        if (lo is double min && angle < min || hi is double max && angle > max) return false;
        if (_angleCorrectionDeg == 0)
        {
            xDisp = point.X + _xOffsetMm;
            yPlane = point.Y;
        }
        else
        {
            double radians = _angleCorrectionDeg * (Math.PI / 180);
            double cosine = Math.Cos(radians), sine = Math.Sin(radians);
            xDisp = point.X * cosine - point.Y * sine + _xOffsetMm;
            yPlane = point.X * sine + point.Y * cosine;
        }
        return true;
    }

    private bool HasNativePoints => _profileSnapPoints?.OfType<ScannerPlanePoint>().Any() == true;

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
    private static readonly Pen RegionPen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB)), 1.6)
    {
        DashStyle = new DashStyle(new[] { 6.0, 3.0 }, 0),
    });
    private static readonly Pen DesktopCalibrationLinePen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0x16, 0xA3, 0x4A)), 2.0));
    private static readonly Brush ProfileDotBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x9F, 0x12, 0x39)));
    private static readonly Brush BaselinePen = Freeze(new SolidColorBrush(Color.FromArgb(180, 0x9C, 0xA3, 0xAF)));
    private static readonly Typeface LabelTypeface = new("Segoe UI");

    /// <summary>距離有效性：0 = 無回波、1 = 雜訊、>= 999000 = 飽和/超量程。</summary>
    private static bool IsValidDistance(int d)
    {
        return d > 1 && d < 999000;
    }

    private static T Freeze<T>(T f) where T : Freezable
    {
        f.Freeze();
        return f;
    }

    public LidarFrame? Frame
    {
        get
        {
            return _frame;
        }
        set { _frame = value; InvalidateVisual(); }
    }

    public IEnumerable? Materials
    {
        get
        {
            return _materials;
        }
        set { _materials = value; InvalidateVisual(); }
    }

    /// <summary>即時或凍結輪廓的原生 double XY 點，不經極座標分箱或整數距離換算。</summary>
    public IEnumerable? ProfileSnapPoints
    {
        get
        {
            return _profileSnapPoints;
        }
        set
        {
            _profileSnapPoints = value;
            InvalidateVisual();
        }
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

        DrawGrid(dc);
        DrawBaseline(dc);

        if (HasNativePoints || _frame is not null && _frame.Cartesian.Length >= 2)
        {
            DrawProfile(dc);
        }
    }

    private void ComputeLayoutAndRanges()
    {
        _plotRect = new Rect(MarginLeft, MarginTop,
            Math.Max(0, ActualWidth - MarginLeft - MarginRight),
            Math.Max(0, ActualHeight - MarginTop - MarginBottom));
        var points = new List<(double X, double Y)>();
        if (HasNativePoints)
        {
            foreach (var point in _profileSnapPoints!.OfType<ScannerPlanePoint>())
                if (TryNativePlaneXY(point, out double x, out double y)) points.Add((x, y));
        }
        else if (_frame is LidarFrame frame)
        {
            for (int i = 0; i < frame.Distances.Length; i++)
                if (TryPlaneXY(frame, i, out double x, out double y)) points.Add((x, y));
        }
        if (points.Count == 0)
        {
            _baselineY = _absBase = 0;
            _xMin = _useFixedPlotRange ? _fixedXMin : -500;
            _xMax = _useFixedPlotRange ? _fixedXMax : 500;
            _zMin = _useFixedPlotRange ? _fixedZMin : 0;
            _zMax = _useFixedPlotRange ? _fixedZMax : 50;
            return;
        }
        var ys = points.Select(p => p.Y).Order().ToArray();
        _baselineY = _referenceYMm ?? ys[ys.Length / 2];
        _absBase = Math.Abs(_baselineY);
        if (_useFixedPlotRange)
        {
            _xMin = _fixedXMin; _xMax = _fixedXMax;
            _zMin = _fixedZMin; _zMax = _fixedZMax;
            return;
        }
        double xmin = points.Min(p => p.X), xmax = points.Max(p => p.X);
        double xPad = Math.Max((xmax - xmin) * .05, 1);
        _xMin = xmin - xPad; _xMax = xmax + xPad;
        double zMin = Math.Min(0, points.Min(p => _absBase - Math.Abs(p.Y) + _zOffsetMm));
        double zMax = Math.Max(0, points.Max(p => _absBase - Math.Abs(p.Y) + _zOffsetMm));
        _zMin = zMin - Math.Max(-zMin * .12, 8);
        _zMax = Math.Max(zMax + Math.Max(Math.Abs(zMax) * .12, 8), _zMin + 50);
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
        List<List<Point>> segments;
        List<Point> allDots;
        if (TryBuildSnapPointProfile(out segments, out allDots))
        {
            dc.PushClip(new RectangleGeometry(_plotRect));
            DrawProfileGeometry(dc, segments, allDots);
            DrawSensorRegion(dc);
            DrawDesktopMarkers(dc);
            dc.Pop();
            return;
        }

        if (_frame is not LidarFrame f) return;
        int n = f.Distances.Length;

        // 把連續的有效點切成一段段線段；遇到無效點就「斷線」，避免畫面從 0,0 拉一條長線過去。
        segments = new List<List<Point>>();
        var current = new List<Point>(n);
        allDots = new List<Point>(n);
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

        dc.PushClip(new RectangleGeometry(_plotRect));
        DrawProfileGeometry(dc, segments, allDots);
        DrawSensorRegion(dc);
        DrawDesktopMarkers(dc);
        dc.Pop();
    }

    private void DrawProfileGeometry(DrawingContext dc, List<List<Point>> segments, List<Point> allDots)
    {
        if (segments.Count > 0)
        {
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
            dc.DrawGeometry(null, ProfilePen, geo);

        }
        foreach (var p in allDots)
        {
            if (p.X < _plotRect.Left - 2 || p.X > _plotRect.Right + 2) continue;
            if (p.Y < _plotRect.Top - 2 || p.Y > _plotRect.Bottom + 2) continue;
            dc.DrawEllipse(ProfileDotBrush, null, p, 1.0, 1.0);
        }
    }

    /// <summary>依掃描器 XY 凍結點繪製連續剖面（角度排序；間距過大則斷線）。</summary>
    private bool TryBuildSnapPointProfile(out List<List<Point>> segments, out List<Point> allDots)
    {
        segments = new List<List<Point>>();
        allDots = new List<Point>();
        if (_profileSnapPoints is null)
        {
            return false;
        }

        var ordered = new List<(double AngleDeg, Point Screen)>();
        foreach (var item in _profileSnapPoints)
        {
            if (item is not ScannerPlanePoint pt)
            {
                continue;
            }

            if (!TryNativePlaneXY(pt, out double x, out double y)) continue;
            double z = _absBase - Math.Abs(y) + _zOffsetMm;
            var screen = DataToScreen(x, z);
            double angleDeg = Math.Atan2(y, x - _xOffsetMm) * 180.0 / Math.PI;
            ordered.Add((angleDeg, screen));
            allDots.Add(screen);
        }

        if (ordered.Count == 0) return false;
        if (ordered.Count == 1) return true;

        ordered.Sort((left, right) => left.AngleDeg.CompareTo(right.AngleDeg));
        var steps = new List<double>();
        for (var i = 1; i < ordered.Count; i++)
        {
            var step = ordered[i].AngleDeg - ordered[i - 1].AngleDeg;
            if (step > 0.001)
            {
                steps.Add(step);
            }
        }

        double breakDeg = 1.0;
        if (steps.Count > 0)
        {
            steps.Sort();
            breakDeg = Math.Max(steps[steps.Count / 2] * 3.0, 0.5);
        }

        var current = new List<Point> { ordered[0].Screen };
        for (var i = 1; i < ordered.Count; i++)
        {
            if (ordered[i].AngleDeg - ordered[i - 1].AngleDeg > breakDeg)
            {
                if (current.Count >= 2)
                {
                    segments.Add(current);
                }

                current = new List<Point>();
            }

            current.Add(ordered[i].Screen);
        }

        if (current.Count >= 2)
        {
            segments.Add(current);
        }

        return allDots.Count > 0;
    }

    private void DrawDesktopMarkers(DrawingContext dc)
    {
        if (_plotRect.Width <= 0 || _plotRect.Height <= 0)
        {
            return;
        }

        DrawDesktopMarker(dc, DesktopFirstX, DesktopFirstY, Brushes.Lime);
        DrawDesktopMarker(dc, DesktopSecondX, DesktopSecondY, Brushes.DeepSkyBlue);
        if (DesktopFirstX.HasValue && DesktopFirstY.HasValue && DesktopSecondX.HasValue && DesktopSecondY.HasValue
            && TryDesktopScreenPoint(DesktopFirstX.Value, DesktopFirstY.Value, out var p1)
            && TryDesktopScreenPoint(DesktopSecondX.Value, DesktopSecondY.Value, out var p2))
        {
            dc.DrawLine(DesktopCalibrationLinePen, p1, p2);
        }
    }

    private bool TryDesktopScreenPoint(double x, double y, out Point screen)
    {
        screen = new Point();
        if (_plotRect.Width <= 0 || _plotRect.Height <= 0)
        {
            return false;
        }

        if (!TryNativePlaneXY(new ScannerPlanePoint { X = x, Y = y }, out double displayX, out double planeY)) return false;
        double z = _absBase - Math.Abs(planeY) + _zOffsetMm;
        screen = DataToScreen(displayX, z);
        return true;
    }

    private void DrawDesktopMarker(DrawingContext dc, double? x, double? y, Brush fill)
    {
        if (!x.HasValue || !y.HasValue)
        {
            return;
        }

        if (!TryDesktopScreenPoint(x.Value, y.Value, out var p))
        {
            return;
        }

        dc.DrawEllipse(fill, new Pen(Brushes.White, 1.5), p, 6, 6);
    }

    /// <summary>畫出框選矩形，座標跟著目前剖面的換算。</summary>
    private void DrawSensorRegion(DrawingContext dc)
    {
        if (_region is null || _plotRect.Width <= 0 || _plotRect.Height <= 0)
        {
            return;
        }

        var corners = new[]
        {
            new ScannerPlanePoint { X = _region.XMin, Y = _region.YMin },
            new ScannerPlanePoint { X = _region.XMax, Y = _region.YMin },
            new ScannerPlanePoint { X = _region.XMax, Y = _region.YMax },
            new ScannerPlanePoint { X = _region.XMin, Y = _region.YMax }
        };
        var polygon = new List<Point>();
        foreach (var corner in corners)
        {
            if (!TryNativePlaneXY(corner, out double x, out double y)) return;
            polygon.Add(DataToScreen(x, _absBase - Math.Abs(y) + _zOffsetMm));
        }
        var geometry = new StreamGeometry();
        using (var path = geometry.Open())
        {
            path.BeginFigure(polygon[0], false, true);
            path.PolyLineTo(polygon.Skip(1).ToList(), true, false);
        }
        dc.DrawGeometry(null, RegionPen, geometry);
    }

    private FormattedText MakeLabel(string text)
    {
        return new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            LabelTypeface, 11, LabelBrush, 1.0);
    }

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
