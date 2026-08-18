using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace ThreeDScan.Controls;

public partial class GlbMeshViewport : UserControl
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
    private ViewportBoundsAnnotations? _annotations;
    private GeometryModel3D? _meshModel;

    public static readonly DependencyProperty GlbPathProperty = DependencyProperty.Register(
        nameof(GlbPath),
        typeof(string),
        typeof(GlbMeshViewport),
        new PropertyMetadata(null, OnGlbPathChanged));

    public string? GlbPath
    {
        get => (string?)GetValue(GlbPathProperty);
        set => SetValue(GlbPathProperty, value);
    }

    public GlbMeshViewport()
    {
        InitializeComponent();
        MouseLayer.LostMouseCapture += (_, _) =>
        {
            _orbitDragging = false;
            _didDrag = false;
        };
        Loaded += (_, _) => _annotations ??= new ViewportBoundsAnnotations(Viewport, AnnotationOverlay);
    }

    private static void OnGlbPathChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is GlbMeshViewport self)
            self.LoadPath(e.NewValue as string);
    }

    public void LoadPath(string? path)
    {
        Viewport.Children.Clear();
        _meshModel = null;
        _annotations ??= new ViewportBoundsAnnotations(Viewport, AnnotationOverlay);
        _annotations.Clear();
        _hasOrbitScene = false;
        _orbitDragging = false;
        _didDrag = false;
        MouseLayer.ReleaseMouseCapture();

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            EmptyHint.Visibility = Visibility.Visible;
            MouseLayer.Cursor = Cursors.Arrow;
            return;
        }

        try
        {
            var mesh = GlbMeshLoader.LoadFromFile(path);
            ShowMesh(mesh);
        }
        catch (Exception ex)
        {
            EmptyHint.Visibility = Visibility.Visible;
            MouseLayer.Cursor = Cursors.Arrow;
            MessageBox.Show(ex.Message, "GLB 載入失敗", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ShowMesh(GlbMeshLoader.MeshData data)
    {
        EmptyHint.Visibility = Visibility.Collapsed;
        MouseLayer.Cursor = Cursors.Hand;

        var bounds = data.Bounds;
        var center = new Point3D(
            bounds.X + bounds.SizeX * 0.5,
            bounds.Y + bounds.SizeY * 0.5,
            bounds.Z + bounds.SizeZ * 0.5);
        var diagonal = new Vector3D(bounds.SizeX, bounds.SizeY, bounds.SizeZ).Length;
        if (diagonal < 1e-6) diagonal = 100;

        const double fovDeg = 60.0;
        var fovRad = fovDeg * Math.PI / 180.0;
        var dist = (diagonal * 0.5) / Math.Tan(fovRad / 2.0) * 1.35;
        if (dist < 1e-3 || double.IsNaN(dist)) dist = diagonal * 1.5;

        var eyeDir = new Vector3D(1, 1, 1);
        eyeDir.Normalize();
        var camPos = center + eyeDir * dist;

        Viewport.Camera = new PerspectiveCamera
        {
            Position = camPos,
            LookDirection = center - camPos,
            UpDirection = new Vector3D(0, 1, 0),
            FieldOfView = fovDeg,
        };

        _orbitDiagonal = diagonal;
        InitOrbitFromCamera(center, camPos);
        _hasOrbitScene = true;

        Viewport.Children.Add(new ModelVisual3D
        {
            Content = new AmbientLight(Color.FromRgb(40, 44, 52)),
        });
        Viewport.Children.Add(new ModelVisual3D
        {
            Content = new DirectionalLight(Color.FromRgb(220, 225, 235), new Vector3D(-0.6, -1, -0.4)),
        });
        Viewport.Children.Add(new ModelVisual3D
        {
            Content = new DirectionalLight(Color.FromRgb(90, 100, 120), new Vector3D(0.5, 0.2, 0.8)),
        });

        var geo = new MeshGeometry3D
        {
            Positions = data.Positions,
            Normals = data.Normals,
            TriangleIndices = data.TriangleIndices,
        };

        var material = new MaterialGroup();
        material.Children.Add(new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(160, 175, 190))));
        material.Children.Add(new SpecularMaterial(new SolidColorBrush(Color.FromRgb(200, 210, 220)), 40));
        material.Freeze();

        _meshModel = new GeometryModel3D
        {
            Geometry = geo,
            Material = material,
            BackMaterial = material,
        };
        Viewport.Children.Add(new ModelVisual3D { Content = _meshModel });

        _annotations ??= new ViewportBoundsAnnotations(Viewport, AnnotationOverlay);
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
            look = new Vector3D(0, 0, -1);
        else
            look.Normalize();

        var worldUp = Math.Abs(look.Y) > 0.95 ? new Vector3D(0, 0, 1) : new Vector3D(0, 1, 0);
        var right = Vector3D.CrossProduct(look, worldUp);
        if (right.LengthSquared < 1e-12)
            right = new Vector3D(1, 0, 0);
        right.Normalize();
        var up = Vector3D.CrossProduct(right, look);
        up.Normalize();

        pc.Position = pos;
        pc.LookDirection = look;
        pc.UpDirection = up;
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

    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!_hasOrbitScene) return;
        var factor = e.Delta > 0 ? 0.9 : 1.1;
        _orbitDistance = Math.Clamp(_orbitDistance * factor, _orbitDiagonal * 0.05, _orbitDiagonal * 20);
        ApplyOrbitCamera();
        e.Handled = true;
    }

    private void TryPickAt(Point viewportPoint)
    {
        _annotations ??= new ViewportBoundsAnnotations(Viewport, AnnotationOverlay);

        Point3D? hit = null;
        VisualTreeHelper.HitTest(
            Viewport,
            null,
            result =>
            {
                if (result is RayMeshGeometry3DHitTestResult meshHit
                    && ReferenceEquals(meshHit.ModelHit, _meshModel))
                {
                    hit = meshHit.PointHit;
                    return HitTestResultBehavior.Stop;
                }
                return HitTestResultBehavior.Continue;
            },
            new PointHitTestParameters(viewportPoint));

        if (hit is { } p)
            _annotations.ShowAt(p);
        else
            _annotations.Clear();
    }
}
