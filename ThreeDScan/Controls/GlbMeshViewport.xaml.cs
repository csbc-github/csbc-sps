using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using ThreeDScan.Models;

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
    private bool _loading;
    private Vector3D _orbitUp = new Vector3D(0, 1, 0);
    private bool _autoLookHeadPending;
    private Point3D _cloudMin;
    private Point3D _cloudMax;

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

    public static readonly DependencyProperty HeadFeaturesProperty = DependencyProperty.Register(
        nameof(HeadFeatures),
        typeof(HeadFeatureDisplayInfo),
        typeof(GlbMeshViewport),
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

    public GlbMeshViewport()
    {
        InitializeComponent();
        MouseLayer.LostMouseCapture += (_, _) =>
        {
            _orbitDragging = false;
            _didDrag = false;
        };
        Loaded += (_, _) =>
        {
            _annotations ??= new ViewportBoundsAnnotations(Viewport, AnnotationOverlay);
            RefreshPresentation();
        };
        SizeChanged += (_, _) => RefreshPresentation();
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
            {
                RefreshPresentation();
            }
        };
    }

    private static void OnGlbPathChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is GlbMeshViewport self)
            self.LoadPath(e.NewValue as string);
    }

    private static void OnHeadFeaturesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not GlbMeshViewport self)
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

        self.UpdateLookHeadButton();
        self.TryApplyDefaultHeadView();
    }

    private void OnHeadFeaturesPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        UpdateLookHeadButton();
        TryApplyDefaultHeadView();
    }

    private void RefreshPresentation()
    {
        if (_loading || !IsVisible || ActualWidth < 2 || ActualHeight < 2)
        {
            return;
        }

        if (_hasOrbitScene)
        {
            ApplyOrbitCamera();
            return;
        }

        if (!string.IsNullOrWhiteSpace(GlbPath) && File.Exists(GlbPath))
        {
            LoadPath(GlbPath);
        }
    }

    public void LoadPath(string? path)
    {
        if (_loading)
        {
            return;
        }

        _loading = true;
        try
        {
            Viewport.Children.Clear();
            _meshModel = null;
            _annotations ??= new ViewportBoundsAnnotations(Viewport, AnnotationOverlay);
            _annotations.ClearAll();
            _hasOrbitScene = false;
            _orbitDragging = false;
            _didDrag = false;
            MouseLayer.ReleaseMouseCapture();

            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                EmptyHint.Visibility = Visibility.Visible;
                MouseLayer.Cursor = Cursors.Arrow;
                LookHeadButton.Visibility = Visibility.Collapsed;
                _autoLookHeadPending = false;
                return;
            }

            var mesh = GlbMeshLoader.LoadFromFile(path);
            ShowMesh(mesh);
        }
        catch (Exception ex)
        {
            EmptyHint.Visibility = Visibility.Visible;
            MouseLayer.Cursor = Cursors.Arrow;
            MessageBox.Show(ex.Message, "GLB 載入失敗", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _loading = false;
            if (_hasOrbitScene)
            {
                Dispatcher.BeginInvoke(
                    System.Windows.Threading.DispatcherPriority.Loaded,
                    ApplyOrbitCamera);
            }
        }
    }

    private void ShowMesh(GlbMeshLoader.MeshData data)
    {
        EmptyHint.Visibility = Visibility.Collapsed;
        MouseLayer.Cursor = Cursors.Hand;

        var bounds = data.Bounds;
        _cloudMin = new Point3D(bounds.X, bounds.Y, bounds.Z);
        _cloudMax = new Point3D(bounds.X + bounds.SizeX, bounds.Y + bounds.SizeY, bounds.Z + bounds.SizeZ);
        var center = new Point3D(
            bounds.X + bounds.SizeX * 0.5,
            bounds.Y + bounds.SizeY * 0.5,
            bounds.Z + bounds.SizeZ * 0.5);
        var diagonal = new Vector3D(bounds.SizeX, bounds.SizeY, bounds.SizeZ).Length;
        if (diagonal < 1e-6) diagonal = 100;

        const double fovDeg = 60.0;
        var dist = HeadViewCamera.DistanceForDiagonal(diagonal);
        if (dist < 1e-3 || double.IsNaN(dist)) dist = diagonal * 1.5;

        _orbitUp = new Vector3D(0, 1, 0);
        var eyeDir = new Vector3D(1, 1, 1);
        eyeDir.Normalize();
        var camPos = center + eyeDir * dist;

        Viewport.Camera = new PerspectiveCamera
        {
            Position = camPos,
            LookDirection = center - camPos,
            UpDirection = new Vector3D(0, 1, 0),
            FieldOfView = fovDeg,
            NearPlaneDistance = Math.Max(dist * 0.01, 0.5),
            FarPlaneDistance = Math.Max(dist * 20, 1000),
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
        _annotations.SetSceneDiagonal(_orbitDiagonal);
        _autoLookHeadPending = true;
        UpdateLookHeadButton();
        TryApplyDefaultHeadView();
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
        _annotations?.UpdateOverlay();
    }

    private void OnLookHeadClick(object sender, RoutedEventArgs e)
    {
        if (!_hasOrbitScene)
        {
            return;
        }

        if (!HeadViewCamera.TryReadPoints(HeadFeatures, out var left, out var apex, out var right))
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

        if (!HeadViewCamera.TryReadPoints(HeadFeatures, out var left, out var apex, out var right))
        {
            return;
        }

        _autoLookHeadPending = false;
        LookAtHead(left, apex, right);
    }

    private void LookAtHead(Point3D left, Point3D apex, Point3D right)
    {
        var center = new Point3D(
            (_cloudMin.X + _cloudMax.X) * 0.5,
            (_cloudMin.Y + _cloudMax.Y) * 0.5,
            (_cloudMin.Z + _cloudMax.Z) * 0.5);
        if (!HeadViewCamera.TryCompute(center, _orbitDiagonal, left, apex, right, out var pose)
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

    private void UpdateLookHeadButton()
    {
        var canLook = _hasOrbitScene && HeadViewCamera.TryReadPoints(HeadFeatures, out _, out _, out _);
        LookHeadButton.Visibility = canLook ? Visibility.Visible : Visibility.Collapsed;
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
