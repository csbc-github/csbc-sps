using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using ThreeDScan.Domain.Models;
using ThreeDScan.Models;
using ThreeDScan.ViewModels;

internal static class MergedPointCloudPreviewChecks
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    internal static async Task RunAsync()
    {
        string parent = Path.Combine(Path.GetTempPath(), "ThreeDScan.MergedPointCloudPreview.Regression");
        string work = Path.Combine(parent, Guid.NewGuid().ToString("N"));
        string taskDirectory = Path.Combine(work, "SURFACE-preview-fixture");
        string complete = Path.Combine(taskDirectory, "complete");
        Directory.CreateDirectory(complete);
        SurfaceScanAreaViewModel? vm = null;
        try
        {
            string ply = Path.Combine(complete, "stitched.ply");
            File.WriteAllText(ply, "ply\nformat ascii 1.0\nelement vertex 6\nproperty float x\nproperty float y\nproperty float z\nend_header\n0 0 0\n1 0 1\n2 0 0\n0 10 0\n1 10 1\n2 10 0\n");
            var response = new
            {
                headFeatures = new { leftCorner = new { x = 0, y = 0, z = 0 }, apex = new { x = 1, y = 0, z = 1 }, rightCorner = new { x = 2, y = 0, z = 0 } },
                coordinateFrame = "ConfiguredAbsolute", absoluteCoordinatesCalibrated = false,
            };
            File.WriteAllText(Path.Combine(taskDirectory, "task.json"), JsonSerializer.Serialize(new
            {
                taskId = "SURFACE-preview-fixture", kind = "Surface", stitchMode = "NominalButtJoin", materialThicknessMm = 6,
                segments = new[] { new { response }, new { response } },
            }));
            string pointer = ScanMonitorPaths.GlbPointerFile(work, "printing");
            Directory.CreateDirectory(Path.GetDirectoryName(pointer)!);
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["Storage:ContentsRoot"] = work }).Build();

            // Skip production scanner probes, timers and file watchers. Exercise
            // the actual pointer consumer and its asynchronous loaders directly.
            vm = (SurfaceScanAreaViewModel)RuntimeHelpers.GetUninitializedObject(typeof(SurfaceScanAreaViewModel));
            SetField(vm, "_config", configuration);
            SetField(vm, "_machineArea", "printing");
            SetField(vm, "_headFeatures", new HeadFeatureDisplayInfo());
            SetField(vm, "_pointCloudVertices", Array.Empty<PointCloudRenderVertex>());
            vm.PropertyChanged += (_, _) => System.Windows.Application.Current.Dispatcher.VerifyAccess();
            vm.GlbPath = "previous-result.glb";
            vm.IsGlbMode = true;
            SetField(vm, "_glbWatchVersion", 1);
            File.WriteAllText(pointer, ply);
            Reload(vm);
            Require(vm.Is3DMode && !vm.IsGlbMode && vm.GlbPath is null && vm.LastPlyPath == ply,
                "A merged PLY must select the point-cloud viewer and clear a stale GLB immediately.");
            await Until(() => vm.PointCloudVertices.Count == 6 && vm.HeadFeatures.HasHeadPoints && vm.HeadFeatures.SizeY == 10);
            Require(vm.PointCloudVertices.Any(p => p.Position.Y == 0) && vm.PointCloudVertices.Any(p => p.Position.Y == 10),
                "The displayed cloud must contain both original source sections rather than only the last scan.");
            Require(vm.HeadFeatures.SourceLabel.Contains("共2段") && vm.HeadFeatures.ModelThicknessMm == 6
                && vm.HeadFeatures.Apex.Z == 1 && vm.ScanStatusText.Contains("拼接點雲"),
                "Merged-cloud preview must retain the same task metadata, dimensions and saved head features.");

            string unsupported = Path.Combine(complete, "result.json");
            File.WriteAllText(unsupported, "{}");
            File.WriteAllText(pointer, unsupported);
            Reload(vm);
            Require(vm.LastPlyPath == ply && vm.Is3DMode && vm.PointCloudVertices.Count == 6,
                "An unsupported pointer extension must not clear the current point-cloud result.");

            string glb = Path.Combine(complete, "stitched.glb");
            File.WriteAllBytes(glb, [0x67, 0x6c, 0x54, 0x46]);
            SetField(vm, "_glbWatchVersion", 2);
            File.WriteAllText(pointer, glb);
            Reload(vm);
            Require(vm.IsGlbMode && !vm.Is3DMode && vm.GlbPath == glb && vm.LastPlyPath == glb,
                "The existing GLB pointer path must continue selecting the model viewer.");
            await Until(() => vm.PointCloudVertices.Count == 6 && vm.HeadFeatures.SizeY == 10);

            SetField(vm, "_glbWatchVersion", 3);
            File.WriteAllText(pointer, ply);
            Reload(vm);
            await Until(() => vm.PointCloudVertices.Count == 6 && vm.HeadFeatures.SizeY == 10);
            string oldPly = Path.Combine(complete, "old.ply");
            File.WriteAllText(oldPly, "ply\nformat ascii 1.0\nelement vertex 1\nproperty float x\nproperty float y\nproperty float z\nend_header\n999 999 999\n");
            await (Task)typeof(SurfaceScanAreaViewModel).GetMethod("LoadPointCloudThenApplyAsync", PrivateInstance)!
                .Invoke(vm, [oldPly, 2, CancellationToken.None])!;
            Require(vm.Is3DMode && vm.GlbPath is null && vm.LastPlyPath == ply && vm.PointCloudVertices.Count == 6
                && vm.PointCloudVertices.All(p => p.Position.X < 100),
                "An old asynchronous PLY load must not overwrite the current merged-cloud preview.");
            Console.WriteLine("PASS merged PLY preview, source sections, saved metadata, GLB compatibility and stale-load rejection");
        }
        finally
        {
            var cancellation = vm is null ? null : (CancellationTokenSource?)typeof(SurfaceScanAreaViewModel)
                .GetField("_featureLoadCancellation", PrivateInstance)!.GetValue(vm);
            cancellation?.Cancel();
            cancellation?.Dispose();
            string resolved = Path.GetFullPath(work);
            if (!resolved.StartsWith(Path.GetFullPath(parent) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || !Guid.TryParseExact(Path.GetFileName(resolved), "N", out _))
                throw new IOException("Refusing to remove an unexpected preview fixture directory.");
            Directory.Delete(resolved, recursive: true);
        }
    }

    private static void Reload(SurfaceScanAreaViewModel vm) =>
        typeof(SurfaceScanAreaViewModel).GetMethod("LoadApiGlbPointer", PrivateInstance)!.Invoke(vm, null);

    private static void SetField(SurfaceScanAreaViewModel vm, string name, object value) =>
        typeof(SurfaceScanAreaViewModel).GetField(name, PrivateInstance)!.SetValue(vm, value);

    private static async Task Until(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(8);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline) throw new InvalidOperationException("Merged point-cloud preview did not finish loading.");
            await Task.Delay(20);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
