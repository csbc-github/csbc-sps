using System.Reflection;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using Microsoft.Extensions.Configuration;
using ThreeDScan.Application.Common.Models;
using ThreeDScan.Application.Scan;
using ThreeDScan.Application.Scan.Models;
using ThreeDScan.Controls;
using ThreeDScan.Models;
using ThreeDScan.ViewModels;

internal static class HoleMonitorChecks
{
    internal static async Task RunAsync()
    {
        var reader = new FixtureReader();
        var first = Feature("H01", true, 100, 1);
        var second = Feature("H02", true, 200, 2);
        var partial = Feature("P01", false, 300, 3);
        reader.Snapshot = new HoleBoundarySnapshot
        {
            TaskId = "HOLE-ONE", SelectedFeatureId = "H02", Features = [first, second, partial],
            AbsoluteCoordinatesCalibrated = false,
        };
        var vm = new GrindingViewModel(new ConfigurationBuilder().AddInMemoryCollection().Build(), reader);
        vm.PropertyChanged += (_, _) => System.Windows.Application.Current.Dispatcher.VerifyAccess();
        vm.PathPoints.CollectionChanged += (_, _) => System.Windows.Application.Current.Dispatcher.VerifyAccess();
        await Task.Delay(260);
        Require(vm.ClosedFeatureCount == 2 && vm.PartialFeatureCount == 1 && vm.SelectedFeature?.Id == "H02"
            && vm.CoordinateStatus.Contains("尚未完成外參驗證"),
            "The monitor must count all openings and honor the API-selected complete hole.");
        Require(vm.PathPoints.Count == 1 && vm.PathPoints[0].X == 200 && vm.ScannerBoundaryPoints.Single().X == 2,
            "The table must use the selected public boundary, while the cloud marker retains scanner coordinates.");
        vm.SelectedFeature = vm.Features.Single(f => f.Id == "P01");
        Require(vm.PathPoints.Count == 1 && vm.PathPoints[0].X == 300 && !vm.SelectedFeatureIsClosed
            && vm.SelectedFeatureStatus.Contains("僅供檢視") && vm.ScannerBoundaryPoints.Single().X == 3,
            "A partial opening must be selectable for review without concatenating holes or declaring a complete path.");

        reader.Snapshot = new HoleBoundarySnapshot
        {
            TaskId = "DIAGNOSTIC-HOLE-TEST", CapturedAt = DateTimeOffset.Parse("2026-11-26T16:32:00+08:00"),
            Features = [], Points = [], AbsoluteCoordinatesCalibrated = true,
        };
        await Reload(vm);
        Require(vm.PathPoints.Count == 0 && vm.ScannerBoundaryPoints.Count == 0 && vm.Features.Count == 0
            && vm.SelectedFeatureStatus.Contains("未觀察到") && vm.CoordinateStatus.Contains("已完成外參驗證")
            && vm.MeasurementSourceCaption.Contains("實機驗證掃描") && vm.MeasurementSourceCaption.Contains("16:32:00")
            && vm.MeasurementSourceTooltip.Contains("DIAGNOSTIC-HOLE-TEST"),
            "A new empty result must clear stale coordinates and markers and update calibration status.");

        reader.Snapshot = new HoleBoundarySnapshot { Points = [new CoordinateDto { X = 400, Y = 401, Z = 402 }] };
        await Reload(vm);
        Require(vm.PathPoints.Single().X == 400 && vm.ScannerBoundaryPoints.Count == 0 && vm.CoordinateStatus.Contains("未提供外參驗證狀態"),
            "Legacy absolute-only boundaries must remain visible in the table without a fabricated scanner overlay.");

        System.Windows.Application.Current.Resources["OverlayActionButtonStyle"] = new System.Windows.Style(typeof(Button));
        var view = new PointCloudViewport
        {
            Vertices = new[]
            {
                new PointCloudRenderVertex(new Point3D(0, 0, 0), Colors.Gray),
                new PointCloudRenderVertex(new Point3D(10, 0, 0), Colors.Gray),
                new PointCloudRenderVertex(new Point3D(0, 10, 0), Colors.Gray),
            },
        };
        var viewport = (Viewport3D)view.FindName("Viewport");
        var camera = viewport.Camera;
        view.HighlightIsClosed = true;
        view.HighlightPoints = new[] { new Point3D(2, 3, 4) };
        var model = (GeometryModel3D)typeof(PointCloudViewport).GetField("_highlightModel", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(view)!;
        var mesh = (MeshGeometry3D)model.Geometry;
        Require(ReferenceEquals(camera, viewport.Camera) && mesh.Positions.Count == 4,
            "Changing the selected boundary must retain the orbit camera and create only measured point markers.");
        var center = new Point3D(mesh.Positions.Average(p => p.X), mesh.Positions.Average(p => p.Y), mesh.Positions.Average(p => p.Z));
        Require((center - new Point3D(2, 3, 4)).Length < 1e-10,
            "The boundary marker must be centered at its native scanner XYZ, without an absolute-coordinate transform.");
        Console.WriteLine("PASS hole monitor selection, empty-result clearing, coordinate separation and camera-preserving markers");
    }

    private static HoleFeatureSnapshot Feature(string id, bool closed, double absolute, double scanner) => new()
    {
        Id = id, Closed = closed, Status = closed ? "CLOSED" : "PARTIAL", FaceId = "F01",
        Boundary = [new CoordinateDto { X = absolute, Y = absolute + 1, Z = absolute + 2 }],
        ScannerBoundary = [new CoordinateDto { X = scanner, Y = scanner + 1, Z = scanner + 2 }],
    };

    private static async Task Reload(GrindingViewModel vm)
    {
        int version = (int)typeof(GrindingViewModel).GetField("_watchVersion", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(vm)!;
        await (Task)typeof(GrindingViewModel).GetMethod("ApplyBoundaryAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm, [version])!;
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
    private sealed class FixtureReader : IHoleBoundaryReader
    {
        internal HoleBoundarySnapshot? Snapshot { get; set; }
        public HoleBoundarySnapshot? TryRead(string contentsRoot) => Snapshot;
    }
}
