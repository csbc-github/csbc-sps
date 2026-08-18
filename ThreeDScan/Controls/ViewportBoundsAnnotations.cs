using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Shapes;

namespace ThreeDScan.Controls;

/// <summary>
/// 點選座標標註：固定 3D 錨點＋每幀重投影到 Canvas 的 2D Callout。
/// </summary>
internal sealed class ViewportBoundsAnnotations
{
    private readonly Viewport3D _viewport;
    private readonly Canvas _overlay;
    private Callout? _callout;
    private ModelVisual3D? _markerVisual;
    private bool _active;

    private sealed class Callout
    {
        public required Point3D World { get; init; }
        public required Ellipse Anchor { get; init; }
        public required Line Leader { get; init; }
        public required Border Label { get; init; }
    }

    public ViewportBoundsAnnotations(Viewport3D viewport, Canvas overlay)
    {
        _viewport = viewport;
        _overlay = overlay;
        _viewport.SizeChanged += (_, _) => UpdateOverlay();
        _overlay.SizeChanged += (_, _) => UpdateOverlay();
    }

    public void Clear()
    {
        _active = false;
        _callout = null;
        _overlay.Children.Clear();
        if (_markerVisual is not null)
        {
            if (_viewport.Children.Contains(_markerVisual))
                _viewport.Children.Remove(_markerVisual);
            _markerVisual = null;
        }
    }

    /// <summary>在指定世界座標顯示一組座標 Callout（取代前一次點選）。</summary>
    public void ShowAt(Point3D world)
    {
        Clear();

        var color = Color.FromRgb(80, 220, 160);
        var brush = new SolidColorBrush(color);
        brush.Freeze();

        var diagonal = EstimateSceneDiagonal();
        var markerRadius = Math.Max(diagonal * 0.008, 0.25);

        _markerVisual = CreateMarkerVisual(world, markerRadius, color);
        _viewport.Children.Add(_markerVisual);

        var anchor = new Ellipse
        {
            Width = 8,
            Height = 8,
            Fill = brush,
            Stroke = Brushes.White,
            StrokeThickness = 1.2,
            IsHitTestVisible = false,
        };
        var leader = new Line
        {
            Stroke = brush,
            StrokeThickness = 1.4,
            IsHitTestVisible = false,
        };
        var label = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(220, 16, 18, 22)),
            BorderBrush = brush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(8, 4, 8, 4),
            IsHitTestVisible = false,
            Child = new TextBlock
            {
                Text = FormatPoint(world),
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
                Foreground = brush,
            },
        };

        _overlay.Children.Add(leader);
        _overlay.Children.Add(anchor);
        _overlay.Children.Add(label);

        _callout = new Callout
        {
            World = world,
            Anchor = anchor,
            Leader = leader,
            Label = label,
        };
        _active = true;

        _overlay.Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Render,
            UpdateOverlay);
    }

    public void UpdateOverlay()
    {
        if (!_active || _callout is null || _overlay.ActualWidth <= 1 || _overlay.ActualHeight <= 1)
            return;

        if (!TryProjectMarker(out var anchor))
        {
            _callout.Anchor.Visibility = Visibility.Collapsed;
            _callout.Leader.Visibility = Visibility.Collapsed;
            _callout.Label.Visibility = Visibility.Collapsed;
            return;
        }

        _callout.Anchor.Visibility = Visibility.Visible;
        _callout.Leader.Visibility = Visibility.Visible;
        _callout.Label.Visibility = Visibility.Visible;

        Canvas.SetLeft(_callout.Anchor, anchor.X - _callout.Anchor.Width / 2);
        Canvas.SetTop(_callout.Anchor, anchor.Y - _callout.Anchor.Height / 2);

        var labelSize = MeasureLabel(_callout.Label);
        const double offsetX = 28;
        const double offsetY = -36;
        var labelPos = new Point(anchor.X + offsetX, anchor.Y + offsetY);
        labelPos.X = Math.Clamp(labelPos.X, 4, Math.Max(4, _overlay.ActualWidth - labelSize.Width - 4));
        labelPos.Y = Math.Clamp(labelPos.Y, 4, Math.Max(4, _overlay.ActualHeight - labelSize.Height - 4));

        // 引線接到標籤左緣中點（或右緣，若標籤在錨點左側）
        var labelCenterY = labelPos.Y + labelSize.Height * 0.5;
        double x2, y2;
        if (labelPos.X + labelSize.Width * 0.5 >= anchor.X)
        {
            x2 = labelPos.X;
            y2 = labelCenterY;
        }
        else
        {
            x2 = labelPos.X + labelSize.Width;
            y2 = labelCenterY;
        }

        _callout.Leader.X1 = anchor.X;
        _callout.Leader.Y1 = anchor.Y;
        _callout.Leader.X2 = x2;
        _callout.Leader.Y2 = y2;

        Canvas.SetLeft(_callout.Label, labelPos.X);
        Canvas.SetTop(_callout.Label, labelPos.Y);
    }

    private bool TryProjectMarker(out Point screen)
    {
        screen = default;
        if (_markerVisual is null || _viewport.ActualWidth <= 1)
            return false;

        try
        {
            var transform = _markerVisual.TransformToAncestor(_viewport);
            if (transform is null)
                return TryProjectMatrix(_viewport, _callout!.World, out screen);

            // 球心在 marker local (0,0,0)
            screen = transform.Transform(new Point3D(0, 0, 0));
            return !double.IsNaN(screen.X) && !double.IsNaN(screen.Y)
                   && !double.IsInfinity(screen.X) && !double.IsInfinity(screen.Y);
        }
        catch
        {
            return _callout is not null && TryProjectMatrix(_viewport, _callout.World, out screen);
        }
    }

    private double EstimateSceneDiagonal()
    {
        if (_viewport.Camera is PerspectiveCamera pc)
        {
            var lookLen = pc.LookDirection.Length;
            if (lookLen > 1e-6)
                return Math.Max(lookLen * 2, 50);
        }
        return 100;
    }

    private static ModelVisual3D CreateMarkerVisual(Point3D center, double radius, Color color)
    {
        var mesh = BuildSphere(radius, slices: 10, stacks: 8);
        var mat = new MaterialGroup();
        mat.Children.Add(new DiffuseMaterial(new SolidColorBrush(color)));
        mat.Children.Add(new EmissiveMaterial(new SolidColorBrush(Color.FromArgb(90, color.R, color.G, color.B))));
        mat.Freeze();
        return new ModelVisual3D
        {
            Content = new GeometryModel3D
            {
                Geometry = mesh,
                Material = mat,
                BackMaterial = mat,
            },
            Transform = new TranslateTransform3D(center.X, center.Y, center.Z),
        };
    }

    private static MeshGeometry3D BuildSphere(double radius, int slices, int stacks)
    {
        var mesh = new MeshGeometry3D();
        for (var stack = 0; stack <= stacks; stack++)
        {
            var phi = Math.PI * stack / stacks;
            var y = Math.Cos(phi);
            var r = Math.Sin(phi);
            for (var slice = 0; slice <= slices; slice++)
            {
                var theta = 2 * Math.PI * slice / slices;
                mesh.Positions.Add(new Point3D(
                    radius * r * Math.Cos(theta),
                    radius * y,
                    radius * r * Math.Sin(theta)));
            }
        }

        for (var stack = 0; stack < stacks; stack++)
        {
            for (var slice = 0; slice < slices; slice++)
            {
                var i0 = stack * (slices + 1) + slice;
                var i1 = i0 + slices + 1;
                mesh.TriangleIndices.Add(i0);
                mesh.TriangleIndices.Add(i1);
                mesh.TriangleIndices.Add(i0 + 1);
                mesh.TriangleIndices.Add(i0 + 1);
                mesh.TriangleIndices.Add(i1);
                mesh.TriangleIndices.Add(i1 + 1);
            }
        }

        mesh.Freeze();
        return mesh;
    }

    private static Size MeasureLabel(FrameworkElement label)
    {
        if (label.ActualWidth >= 1 && label.ActualHeight >= 1)
            return new Size(label.ActualWidth, label.ActualHeight);
        label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return label.DesiredSize;
    }

    private static string FormatPoint(Point3D p)
        => string.Format(CultureInfo.InvariantCulture,
            "X = {0:F2} mm\nY = {1:F2} mm\nZ = {2:F2} mm", p.X, p.Y, p.Z);

    private static bool TryProjectMatrix(Viewport3D viewport, Point3D world, out Point screen)
    {
        screen = default;
        if (viewport.Camera is not PerspectiveCamera camera)
            return false;

        var aspect = viewport.ActualWidth / viewport.ActualHeight;
        var m = GetViewMatrix(camera);
        m.Append(GetProjectionMatrix(camera, aspect));
        m.Append(GetHomogeneousToViewportMatrix(viewport.ActualWidth, viewport.ActualHeight));

        var p = m.Transform(new Point4D(world.X, world.Y, world.Z, 1));
        if (Math.Abs(p.W) < 1e-12)
            return false;

        var z = p.Z / p.W;
        if (z < 0 || z > 1.05)
            return false;

        screen = new Point(p.X / p.W, p.Y / p.W);
        return true;
    }

    private static Matrix3D GetViewMatrix(ProjectionCamera camera)
    {
        var zaxis = -camera.LookDirection;
        if (zaxis.LengthSquared < 1e-18)
            zaxis = new Vector3D(0, 0, 1);
        zaxis.Normalize();

        var xaxis = Vector3D.CrossProduct(camera.UpDirection, zaxis);
        if (xaxis.LengthSquared < 1e-12)
            xaxis = Vector3D.CrossProduct(new Vector3D(0, 0, 1), zaxis);
        if (xaxis.LengthSquared < 1e-12)
            xaxis = new Vector3D(1, 0, 0);
        xaxis.Normalize();

        var yaxis = Vector3D.CrossProduct(zaxis, xaxis);
        var pos = (Vector3D)camera.Position;

        return new Matrix3D(
            xaxis.X, yaxis.X, zaxis.X, 0,
            xaxis.Y, yaxis.Y, zaxis.Y, 0,
            xaxis.Z, yaxis.Z, zaxis.Z, 0,
            -Vector3D.DotProduct(xaxis, pos),
            -Vector3D.DotProduct(yaxis, pos),
            -Vector3D.DotProduct(zaxis, pos),
            1);
    }

    private static Matrix3D GetProjectionMatrix(PerspectiveCamera camera, double aspect)
    {
        var fov = camera.FieldOfView * Math.PI / 180.0;
        var zn = camera.NearPlaneDistance > 0 ? camera.NearPlaneDistance : 0.125;
        var zf = double.IsInfinity(camera.FarPlaneDistance) || camera.FarPlaneDistance <= zn
            ? 1e6
            : camera.FarPlaneDistance;

        var xScale = 1.0 / Math.Tan(fov * 0.5);
        var yScale = xScale * Math.Max(aspect, 1e-6);

        return new Matrix3D(
            xScale, 0, 0, 0,
            0, yScale, 0, 0,
            0, 0, zf / (zn - zf), -1,
            0, 0, zn * zf / (zn - zf), 0);
    }

    private static Matrix3D GetHomogeneousToViewportMatrix(double width, double height)
    {
        var sx = width * 0.5;
        var sy = height * 0.5;
        return new Matrix3D(
            sx, 0, 0, 0,
            0, -sy, 0, 0,
            0, 0, 1, 0,
            sx, sy, 0, 1);
    }

    /// <summary>螢幕點 → 世界空間射線（供點雲拾取）。</summary>
    public static bool TryGetPickRay(Viewport3D viewport, Point screenPoint, out Point3D origin, out Vector3D direction)
    {
        origin = default;
        direction = default;
        if (viewport.Camera is not PerspectiveCamera camera)
            return false;
        if (viewport.ActualWidth <= 1 || viewport.ActualHeight <= 1)
            return false;

        var aspect = viewport.ActualWidth / viewport.ActualHeight;
        var view = GetViewMatrix(camera);
        var proj = GetProjectionMatrix(camera, aspect);
        var vp = GetHomogeneousToViewportMatrix(viewport.ActualWidth, viewport.ActualHeight);

        var m = view;
        m.Append(proj);
        m.Append(vp);
        if (!m.HasInverse)
            return false;
        m.Invert();

        var near4 = m.Transform(new Point4D(screenPoint.X, screenPoint.Y, 0, 1));
        var far4 = m.Transform(new Point4D(screenPoint.X, screenPoint.Y, 1, 1));
        if (Math.Abs(near4.W) < 1e-12 || Math.Abs(far4.W) < 1e-12)
            return false;

        var near = new Point3D(near4.X / near4.W, near4.Y / near4.W, near4.Z / near4.W);
        var far = new Point3D(far4.X / far4.W, far4.Y / far4.W, far4.Z / far4.W);
        origin = near;
        direction = far - near;
        if (direction.LengthSquared < 1e-18)
            return false;
        direction.Normalize();
        return true;
    }

    /// <summary>射線最近點（距離平方門檻內）。</summary>
    public static bool TryPickNearestPoint(
        IReadOnlyList<Point3D> points,
        Point3D rayOrigin,
        Vector3D rayDir,
        double maxDist,
        out Point3D hit)
    {
        hit = default;
        var bestDistSq = maxDist * maxDist;
        var found = false;
        foreach (var p in points)
        {
            var op = p - rayOrigin;
            var t = Vector3D.DotProduct(op, rayDir);
            if (t < 0) continue;
            var closest = rayOrigin + rayDir * t;
            var d = p - closest;
            var distSq = d.LengthSquared;
            if (distSq < bestDistSq)
            {
                bestDistSq = distSq;
                hit = p;
                found = true;
            }
        }
        return found;
    }
}
