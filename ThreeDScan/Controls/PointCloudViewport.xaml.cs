using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using ThreeDScan.Models;

namespace ThreeDScan.Controls;

public partial class PointCloudViewport : UserControl
{
    private const double ClickSlopPx = 5;

    private Point3D _orbitTarget;
    private double _orbitDistance = 1;
    private double _orbitDiagonal = 100;
    private double _yaw;
    private double _pitch;
    private Point _pressPos;
    private Point _lastMouse;
    private bool _orbitDragging;
    private bool _didDrag;
    private bool _hasOrbitScene;

    /// <summary>與幾何一一對應，供相機變動時重算 billboard。</summary>
    private readonly List<(GeometryModel3D Model, List<Point3D> Points)> _pointLayers = new();
    private readonly List<Point3D> _allPoints = new();
    private ViewportBoundsAnnotations? _annotations;

    private double _splatHalfSize = 0.1;
    private bool _billboardRebuildPending;
    private Point3D _cloudMin;
    private Point3D _cloudMax;
    private Vector3D _orbitUp = new Vector3D(0, 1, 0);
    private bool _autoLookHeadPending;

    public static readonly DependencyProperty VerticesProperty = DependencyProperty.Register(
        nameof(Vertices),
        typeof(IReadOnlyList<PointCloudRenderVertex>),
        typeof(PointCloudViewport),
        new PropertyMetadata(null, OnVerticesChanged));

    public IReadOnlyList<PointCloudRenderVertex>? Vertices
    {
        get
        {
            return (IReadOnlyList<PointCloudRenderVertex>?)GetValue(VerticesProperty);
        }
        set
        {
            SetValue(VerticesProperty, value);
        }
    }

    public static readonly DependencyProperty HeadFeaturesProperty = DependencyProperty.Register(
        nameof(HeadFeatures),
        typeof(HeadFeatureDisplayInfo),
        typeof(PointCloudViewport),
        new PropertyMetadata(null, OnHeadFeaturesChanged));

    public HeadFeatureDisplayInfo? HeadFeatures
    {
        get
        {
            return (HeadFeatureDisplayInfo?)GetValue(HeadFeaturesProperty);
        }
        set
        {
            SetValue(HeadFeaturesProperty, value);
        }
    }

    public PointCloudViewport()
    {
        InitializeComponent();
        MouseLayer.LostMouseCapture += (_, _) =>
        {
            _orbitDragging = false;
            _didDrag = false;
        };
        Loaded += (_, _) => _annotations ??= new ViewportBoundsAnnotations(Viewport, AnnotationOverlay);
    }

    private static void OnVerticesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is PointCloudViewport self)
            self.RenderVertices();
    }

    private static void OnHeadFeaturesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not PointCloudViewport self)
        {
            return;
        }

        if (e.OldValue is HeadFeatureDisplayInfo oldInfo)
        {
            oldInfo.PropertyChanged -= self.OnHeadFeaturesPropertyChanged;
        }

        if (e.NewValue is HeadFeatureDisplayInfo newInfo)
        {
            newInfo.PropertyChanged += self.OnHeadFeaturesPropertyChanged;
        }

        self.ApplyHeadFeatures();
        self.TryApplyDefaultHeadView();
    }

    private void OnHeadFeaturesPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        ApplyHeadFeatures();
        TryApplyDefaultHeadView();
    }

    private void RenderVertices()
    {
        Viewport.Children.Clear();
        _pointLayers.Clear();
        _allPoints.Clear();
        _annotations ??= new ViewportBoundsAnnotations(Viewport, AnnotationOverlay);
        _annotations.ClearAll();
        var vertices = Vertices;
        if (vertices is null || vertices.Count == 0)
        {
            _hasOrbitScene = false;
            _orbitDragging = false;
            _didDrag = false;
            MouseLayer.ReleaseMouseCapture();
            MouseLayer.Cursor = Cursors.Arrow;
            EmptyHint.Visibility = Visibility.Visible;
            LookHeadButton.Visibility = Visibility.Collapsed;
            _autoLookHeadPending = false;
            return;
        }

        EmptyHint.Visibility = Visibility.Collapsed;
        MouseLayer.Cursor = Cursors.Hand;

        double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;
        foreach (var v in vertices)
        {
            var p = v.Position;
            if (p.X < minX) minX = p.X;
            if (p.Y < minY) minY = p.Y;
            if (p.Z < minZ) minZ = p.Z;
            if (p.X > maxX) maxX = p.X;
            if (p.Y > maxY) maxY = p.Y;
            if (p.Z > maxZ) maxZ = p.Z;
        }

        _cloudMin = new Point3D(minX, minY, minZ);
        _cloudMax = new Point3D(maxX, maxY, maxZ);
        var cloudExtent = _cloudMax - _cloudMin;
        var diagonal = cloudExtent.Length;
        if (diagonal < 1e-6)
        {
            diagonal = 100;
        }

        SetupOrbitFromCloudBounds();
        _hasOrbitScene = true;

        Viewport.Children.Add(new ModelVisual3D { Content = new AmbientLight(Color.FromRgb(18, 20, 24)) });

        var sx = maxX - minX;
        var sy = maxY - minY;
        var sz = maxZ - minZ;
        var n = vertices.Count;
        var areaMax = Math.Max(sx * sy, Math.Max(sx * sz, sy * sz));
        var planarSpacing = Math.Sqrt(Math.Max(areaMax / Math.Max(n, 1), 1e-18));
        var volSpacing = Math.Pow(Math.Max(sx * sy * sz / Math.Max(n, 1), 1e-18), 1.0 / 3.0);
        // 掃描線資料在面上未必均勻，sqrt(area/n) 常低估間距 → 略放大
        var spacingEst = Math.Max(planarSpacing, volSpacing) * 1.22;
        if (double.IsNaN(spacingEst) || spacingEst < 1e-15)
        {
            spacingEst = Math.Max(diagonal * 0.001, 0.1);
        }

        var halfIdeal = spacingEst * 0.52;
        var halfFloor = Math.Max(diagonal * 0.00008, 0.035);
        var halfCeiling = Math.Max(Math.Min(spacingEst * 0.62, diagonal * 0.0022), halfFloor);
        _splatHalfSize = Math.Clamp(halfIdeal, halfFloor, halfCeiling) * 0.28;

        var byColor = new Dictionary<int, List<Point3D>>();
        foreach (var v in vertices)
        {
            var key = PackColor(v.Color);
            if (!byColor.TryGetValue(key, out var list))
            {
                list = new List<Point3D>();
                byColor[key] = list;
            }
            list.Add(v.Position);
            _allPoints.Add(v.Position);
        }

        foreach (var kv in byColor)
        {
            var brush = new SolidColorBrush(UnpackColor(kv.Key));
            brush.Freeze();
            var mat = new EmissiveMaterial(brush);
            mat.Freeze();
            var geo = new GeometryModel3D
            {
                Material = mat,
                BackMaterial = mat,
                Geometry = new MeshGeometry3D()
            };
            _pointLayers.Add((geo, kv.Value));
            Viewport.Children.Add(new ModelVisual3D { Content = geo });
        }

        RebuildBillboardMeshes();
        _autoLookHeadPending = true;
        ApplyHeadFeatures();
        TryApplyDefaultHeadView();
    }

    private void SetupOrbitFromCloudBounds()
    {
        _orbitUp = new Vector3D(0, 1, 0);
        var center = CloudCenter();
        var extent = _cloudMax - _cloudMin;
        var diagonal = extent.Length;
        if (diagonal < 1e-6)
        {
            diagonal = 100;
        }

        var dist = OrbitDistanceForDiagonal(diagonal);
        var eyeDir = new Vector3D(1, 1, 1);
        eyeDir.Normalize();
        var camPos = center + eyeDir * dist;

        Viewport.Camera = new PerspectiveCamera
        {
            Position = camPos,
            LookDirection = center - camPos,
            UpDirection = new Vector3D(0, 1, 0),
            FieldOfView = 60.0
        };

        _orbitDiagonal = diagonal;
        InitOrbitFromCamera(center, camPos);
    }

    private Point3D CloudCenter()
    {
        return new Point3D(
            (_cloudMin.X + _cloudMax.X) * 0.5,
            (_cloudMin.Y + _cloudMax.Y) * 0.5,
            (_cloudMin.Z + _cloudMax.Z) * 0.5);
    }

    private static double OrbitDistanceForDiagonal(double diagonal)
    {
        const double fovDeg = 60.0;
        var fovRad = fovDeg * Math.PI / 180.0;
        var dist = (diagonal * 0.5) / Math.Tan(fovRad / 2.0) * 1.12;
        if (dist < 1e-3 || double.IsNaN(dist))
        {
            return diagonal * 1.5;
        }

        return dist;
    }

    private void OnLookHeadClick(object sender, RoutedEventArgs e)
    {
        if (!_hasOrbitScene)
        {
            return;
        }

        if (!TryGetHeadPoints(out var left, out var apex, out var right))
        {
            return;
        }

        LookAtHead(left, apex, right);
    }

    private void TryApplyDefaultHeadView()
    {
        if (!_autoLookHeadPending || !_hasOrbitScene)
        {
            return;
        }

        if (!TryGetHeadPoints(out var left, out var apex, out var right))
        {
            return;
        }

        _autoLookHeadPending = false;
        LookAtHead(left, apex, right);
    }

    private bool TryGetHeadPoints(out Point3D left, out Point3D apex, out Point3D right)
    {
        return HeadViewCamera.TryReadPoints(HeadFeatures, out left, out apex, out right);
    }

    /// <summary>整支入鏡；相機在側前方，前緣三點在頭側、包角朝上。</summary>
    private void LookAtHead(Point3D left, Point3D apex, Point3D right)
    {
        if (!HeadViewCamera.TryCompute(CloudCenter(), _orbitDiagonal, left, apex, right, out var pose)
            || pose is null)
        {
            return;
        }

        _orbitUp = pose.OrbitUp;
        if (Viewport.Camera is not PerspectiveCamera)
        {
            Viewport.Camera = new PerspectiveCamera
            {
                FieldOfView = 60.0
            };
        }

        InitOrbitFromCamera(pose.Target, pose.CameraPosition);
        ApplyOrbitCamera();
    }

    private void ApplyHeadFeatures()
    {
        if (_annotations is null)
        {
            return;
        }

        var canLook = _hasOrbitScene && TryGetHeadPoints(out var left, out var apex, out var right);
        LookHeadButton.Visibility = canLook ? Visibility.Visible : Visibility.Collapsed;
        if (!canLook)
        {
            _annotations.ClearHeadFeatures();
            return;
        }

        _annotations.SetHeadFeatures(left, apex, right, _orbitDiagonal);
    }

    private void GetOrbitBasis(out Vector3D xAxis, out Vector3D yAxis, out Vector3D zAxis)
    {
        yAxis = _orbitUp;
        if (yAxis.LengthSquared < 1e-18)
        {
            yAxis = new Vector3D(0, 1, 0);
        }

        yAxis.Normalize();
        var hint = new Vector3D(0, 0, 1);
        if (Math.Abs(Vector3D.DotProduct(yAxis, hint)) > 0.9)
        {
            hint = new Vector3D(1, 0, 0);
        }

        xAxis = Vector3D.CrossProduct(yAxis, hint);
        if (xAxis.LengthSquared < 1e-12)
        {
            xAxis = new Vector3D(1, 0, 0);
        }

        xAxis.Normalize();
        zAxis = Vector3D.CrossProduct(xAxis, yAxis);
        zAxis.Normalize();
    }

    private void RequestBillboardRebuild()
    {
        if (!_hasOrbitScene || _pointLayers.Count == 0)
        {
            return;
        }

        if (_billboardRebuildPending)
        {
            return;
        }

        _billboardRebuildPending = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Render, () =>
        {
            _billboardRebuildPending = false;
            RebuildBillboardMeshes();
        });
    }

    private void RebuildBillboardMeshes()
    {
        if (Viewport.Camera is not PerspectiveCamera cam)
        {
            return;
        }

        foreach (var layer in _pointLayers)
        {
            layer.Model.Geometry = BuildBillboardMesh(layer.Points, _splatHalfSize, cam);
        }
    }

    /// <summary>每點兩個小三角形朝向相機（小點）。</summary>
    private static MeshGeometry3D BuildBillboardMesh(IReadOnlyList<Point3D> points, double halfSize, PerspectiveCamera cam)
    {
        var mesh = new MeshGeometry3D();
        var camPos = cam.Position;

        foreach (var p in points)
        {
            var view = camPos - p;
            if (view.LengthSquared < 1e-18) continue;
            view.Normalize();

            var worldUp = new Vector3D(0, 1, 0);
            var right = Vector3D.CrossProduct(worldUp, view);
            if (right.LengthSquared < 1e-14)
            {
                right = Vector3D.CrossProduct(new Vector3D(1, 0, 0), view);
            }
            right.Normalize();
            var up = Vector3D.CrossProduct(view, right);
            up.Normalize();

            right *= halfSize;
            up *= halfSize;

            var i0 = mesh.Positions.Count;
            mesh.Positions.Add(p - right - up);
            mesh.Positions.Add(p + right - up);
            mesh.Positions.Add(p + right + up);
            mesh.Positions.Add(p - right + up);

            AddQuad(mesh, i0, i0 + 1, i0 + 2, i0 + 3);
        }

        mesh.Freeze();
        return mesh;
    }

    private void InitOrbitFromCamera(Point3D target, Point3D cameraPosition)
    {
        _orbitTarget = target;
        var off = cameraPosition - target;
        _orbitDistance = off.Length;
        if (_orbitDistance < 1e-6)
        {
            _orbitDistance = 1;
        }

        GetOrbitBasis(out var xAxis, out var yAxis, out var zAxis);
        var nx = Vector3D.DotProduct(off, xAxis) / _orbitDistance;
        var ny = Vector3D.DotProduct(off, yAxis) / _orbitDistance;
        var nz = Vector3D.DotProduct(off, zAxis) / _orbitDistance;
        _pitch = Math.Clamp(Math.Asin(Math.Clamp(ny, -1.0, 1.0)), -1.48, 1.48);
        _yaw = Math.Atan2(nx, nz);
    }

    private void ApplyOrbitCamera()
    {
        if (Viewport.Camera is not PerspectiveCamera pc)
        {
            return;
        }

        GetOrbitBasis(out var xAxis, out var yAxis, out var zAxis);
        var cp = Math.Cos(_pitch);
        var pos = _orbitTarget
            + xAxis * (_orbitDistance * cp * Math.Sin(_yaw))
            + yAxis * (_orbitDistance * Math.Sin(_pitch))
            + zAxis * (_orbitDistance * cp * Math.Cos(_yaw));
        var look = _orbitTarget - pos;
        if (look.LengthSquared < 1e-18)
        {
            look = new Vector3D(0, 0, -1);
        }
        else
        {
            look.Normalize();
        }

        var right = Vector3D.CrossProduct(look, yAxis);
        if (right.LengthSquared < 1e-12)
        {
            right = new Vector3D(1, 0, 0);
        }

        right.Normalize();
        var up = Vector3D.CrossProduct(right, look);
        up.Normalize();

        pc.Position = pos;
        pc.LookDirection = look;
        pc.UpDirection = up;
        RequestBillboardRebuild();
        _annotations?.UpdateOverlay();
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_hasOrbitScene) return;
        _orbitDragging = true;
        _didDrag = false;
        _pressPos = e.GetPosition(MouseLayer);
        _lastMouse = _pressPos;
        MouseLayer.CaptureMouse();
        _ = MouseLayer.Focus();
        e.Handled = true;
    }

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_orbitDragging) return;
        _orbitDragging = false;
        MouseLayer.ReleaseMouseCapture();

        if (!_didDrag)
            TryPickAt(e.GetPosition(Viewport));

        e.Handled = true;
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_orbitDragging || !_hasOrbitScene) return;
        var p = e.GetPosition(MouseLayer);
        if (!_didDrag)
        {
            var dx0 = p.X - _pressPos.X;
            var dy0 = p.Y - _pressPos.Y;
            if (dx0 * dx0 + dy0 * dy0 < ClickSlopPx * ClickSlopPx)
                return;
            _didDrag = true;
            _lastMouse = p;
        }

        var dx = p.X - _lastMouse.X;
        var dy = p.Y - _lastMouse.Y;
        _lastMouse = p;
        const double sens = 0.005;
        _yaw -= dx * sens;
        _pitch -= dy * sens;
        _pitch = Math.Clamp(_pitch, -1.48, 1.48);
        ApplyOrbitCamera();
        e.Handled = true;
    }

    private void TryPickAt(Point viewportPoint)
    {
        _annotations ??= new ViewportBoundsAnnotations(Viewport, AnnotationOverlay);
        if (_allPoints.Count == 0)
            return;

        if (!ViewportBoundsAnnotations.TryGetPickRay(Viewport, viewportPoint, out var origin, out var dir))
            return;

        // 拾取半徑隨場景尺度與目前 splat 大小
        var maxDist = Math.Max(_splatHalfSize * 4, _orbitDiagonal * 0.01);
        if (ViewportBoundsAnnotations.TryPickNearestPoint(_allPoints, origin, dir, maxDist, out var hit))
            _annotations.ShowAt(hit);
        else
            _annotations.Clear();
    }

    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!_hasOrbitScene) return;
        var factor = e.Delta > 0 ? 0.92 : 1.08;
        var minD = Math.Max(_orbitDiagonal * 0.04, 1e-3);
        var maxD = Math.Max(_orbitDiagonal * 12, minD * 2);
        _orbitDistance = Math.Clamp(_orbitDistance * factor, minD, maxD);
        ApplyOrbitCamera();
        e.Handled = true;
    }

    private static int PackColor(Color c)
    {
        return (c.R << 16) | (c.G << 8) | c.B;
    }

    private static Color UnpackColor(int packed)
    {
        return Color.FromRgb((byte)(packed >> 16), (byte)((packed >> 8) & 255), (byte)(packed & 255));
    }

    private static void AddQuad(MeshGeometry3D mesh, int a, int b, int c, int d)
    {
        mesh.TriangleIndices.Add(a);
        mesh.TriangleIndices.Add(b);
        mesh.TriangleIndices.Add(c);
        mesh.TriangleIndices.Add(a);
        mesh.TriangleIndices.Add(c);
        mesh.TriangleIndices.Add(d);
    }
}
