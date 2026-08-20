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
    private Point3D _orbitTarget;
    private double _orbitDistance = 1;
    private double _orbitDiagonal = 100;
    private double _yaw;
    private double _pitch;
    private Point _lastMouse;
    private bool _orbitDragging;
    private bool _hasOrbitScene;

    /// <summary>與幾何一一對應，供相機變動時重算 billboard。</summary>
    private readonly List<(GeometryModel3D Model, List<Point3D> Points)> _pointLayers = new();

    private double _splatHalfSize = 0.1;
    private bool _billboardRebuildPending;

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

    public PointCloudViewport()
    {
        InitializeComponent();
        MouseLayer.LostMouseCapture += (_, _) => _orbitDragging = false;
    }

    private static void OnVerticesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is PointCloudViewport self)
            self.RenderVertices();
    }

    private void RenderVertices()
    {
        Viewport.Children.Clear();
        _pointLayers.Clear();
        var vertices = Vertices;
        if (vertices is null || vertices.Count == 0)
        {
            _hasOrbitScene = false;
            _orbitDragging = false;
            MouseLayer.ReleaseMouseCapture();
            MouseLayer.Cursor = Cursors.Arrow;
            EmptyHint.Visibility = Visibility.Visible;
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

        var cx = (minX + maxX) * 0.5;
        var cy = (minY + maxY) * 0.5;
        var cz = (minZ + maxZ) * 0.5;
        var center = new Point3D(cx, cy, cz);
        var extent = new Vector3D(maxX - minX, maxY - minY, maxZ - minZ);
        var diagonal = extent.Length;
        if (diagonal < 1e-6) diagonal = 100;

        const double fovDeg = 60.0;
        var fovRad = fovDeg * Math.PI / 180.0;
        var dist = (diagonal * 0.5) / Math.Tan(fovRad / 2.0) * 1.12;
        if (dist < 1e-3 || double.IsNaN(dist)) dist = diagonal * 1.5;

        var eyeDir = new Vector3D(1, 1, 1);
        eyeDir.Normalize();
        var camPos = center + eyeDir * dist;

        Viewport.Camera = new PerspectiveCamera
        {
            Position = camPos,
            LookDirection = center - camPos,
            UpDirection = new Vector3D(0, 1, 0),
            FieldOfView = fovDeg
        };

        _orbitDiagonal = diagonal;
        InitOrbitFromCamera(center, camPos);
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
        _splatHalfSize = Math.Clamp(halfIdeal, halfFloor, halfCeiling);

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

        var axisLen = Math.Max(diagonal * 0.08, 1e-3);
        var axisThickness = Math.Max(diagonal * 0.004, 0.15);
        Viewport.Children.Add(CreateAxisModel(new Point3D(0, 0, 0), new Point3D(axisLen, 0, 0), Colors.IndianRed, axisThickness));
        Viewport.Children.Add(CreateAxisModel(new Point3D(0, 0, 0), new Point3D(0, axisLen, 0), Colors.MediumSeaGreen, axisThickness));
        Viewport.Children.Add(CreateAxisModel(new Point3D(0, 0, 0), new Point3D(0, 0, axisLen), Colors.CornflowerBlue, axisThickness));
    }

    private void RequestBillboardRebuild()
    {
        if (!_hasOrbitScene || _pointLayers.Count == 0) return;
        if (_billboardRebuildPending) return;
        _billboardRebuildPending = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Render, () =>
        {
            _billboardRebuildPending = false;
            RebuildBillboardMeshes();
        });
    }

    private void RebuildBillboardMeshes()
    {
        if (Viewport.Camera is not PerspectiveCamera cam) return;
        foreach (var layer in _pointLayers)
        {
            var mesh = BuildBillboardMesh(layer.Points, _splatHalfSize, cam);
            layer.Model.Geometry = mesh;
        }
    }

    /// <summary>每點兩個三角形，平面朝向相機，避免立方體深度打架造成的糊邊與閃爍。</summary>
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
        if (_orbitDistance < 1e-6) _orbitDistance = 1;
        off.Normalize();
        _pitch = Math.Clamp(Math.Asin(off.Y), -1.48, 1.48);
        _yaw = Math.Atan2(off.X, off.Z);
    }

    private void ApplyOrbitCamera()
    {
        if (Viewport.Camera is not PerspectiveCamera pc) return;
        var cp = Math.Cos(_pitch);
        var x = _orbitDistance * cp * Math.Sin(_yaw);
        var y = _orbitDistance * Math.Sin(_pitch);
        var z = _orbitDistance * cp * Math.Cos(_yaw);
        var pos = _orbitTarget + new Vector3D(x, y, z);
        var look = _orbitTarget - pos;
        if (look.LengthSquared < 1e-18)
        {
            look = new Vector3D(0, 0, -1);
        }
        else
        {
            look.Normalize();
        }
        pc.Position = pos;
        pc.LookDirection = look;
        pc.UpDirection = new Vector3D(0, 1, 0);
        RequestBillboardRebuild();
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_hasOrbitScene) return;
        _orbitDragging = true;
        _lastMouse = e.GetPosition(MouseLayer);
        MouseLayer.CaptureMouse();
        _ = MouseLayer.Focus();
        e.Handled = true;
    }

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_orbitDragging) return;
        _orbitDragging = false;
        MouseLayer.ReleaseMouseCapture();
        e.Handled = true;
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_orbitDragging || !_hasOrbitScene) return;
        var p = e.GetPosition(MouseLayer);
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

    private static ModelVisual3D CreateAxisModel(Point3D a, Point3D b, Color color, double thickness)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        var mat = new EmissiveMaterial(brush);
        mat.Freeze();
        var mesh = BuildLineBox(a, b, thickness);
        return new ModelVisual3D
        {
            Content = new GeometryModel3D { Geometry = mesh, Material = mat, BackMaterial = mat }
        };
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

    private static MeshGeometry3D BuildLineBox(Point3D p0, Point3D p1, double thickness)
    {
        var dir = p1 - p0;
        if (dir.Length < 1e-9) return new MeshGeometry3D();
        dir.Normalize();
        var up = Math.Abs(Vector3D.DotProduct(dir, new Vector3D(0, 1, 0))) > 0.95
            ? new Vector3D(1, 0, 0)
            : new Vector3D(0, 1, 0);
        var right = Vector3D.CrossProduct(dir, up);
        right.Normalize();
        up = Vector3D.CrossProduct(right, dir);
        up.Normalize();

        right *= thickness * 0.5;
        up *= thickness * 0.5;

        var a = p0 - right - up;
        var b = p0 + right - up;
        var c = p0 + right + up;
        var d = p0 - right + up;
        var e = p1 - right - up;
        var f = p1 + right - up;
        var g = p1 + right + up;
        var h = p1 - right + up;

        var mesh = new MeshGeometry3D();
        mesh.Positions.Add(a); mesh.Positions.Add(b); mesh.Positions.Add(c); mesh.Positions.Add(d);
        mesh.Positions.Add(e); mesh.Positions.Add(f); mesh.Positions.Add(g); mesh.Positions.Add(h);
        AddQuad(mesh, 0, 1, 2, 3);
        AddQuad(mesh, 4, 5, 6, 7);
        AddQuad(mesh, 0, 1, 5, 4);
        AddQuad(mesh, 1, 2, 6, 5);
        AddQuad(mesh, 2, 3, 7, 6);
        AddQuad(mesh, 3, 0, 4, 7);
        mesh.Freeze();
        return mesh;
    }
}
