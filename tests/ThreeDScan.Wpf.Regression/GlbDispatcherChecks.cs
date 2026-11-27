using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using ThreeDScan.Controls;

internal static class GlbDispatcherChecks
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    internal static async Task RunAsync()
    {
        Application.Current.Resources["OverlayActionButtonStyle"] = new Style(typeof(Button));
        string parent = Path.Combine(Path.GetTempPath(), "ThreeDScan.GlbDispatcher.Regression");
        string work = Path.Combine(parent, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            var view = new GlbMeshViewport();
            var viewport = (Viewport3D)view.FindName("Viewport");
            var overlay = (Canvas)view.FindName("AnnotationOverlay");
            var description = (TextBlock)view.FindName("EmptyHintDescription");
            string bad = Path.Combine(work, "invalid.glb");
            byte[] badBytes = Encoding.ASCII.GetBytes("This file is deliberately not a binary glTF model.");
            File.WriteAllBytes(bad, badBytes);

            // This is the dispatcher restriction active during WPF layout.
            // Binding the path must enqueue work, never parse or show a modal
            // message box while processing is suspended.
            using (view.Dispatcher.DisableProcessing())
            {
                view.GlbPath = bad;
                Require(view.LoadError is null && viewport.Children.Count == 0,
                    "A path binding must defer parsing and presentation until dispatcher processing resumes.");
            }
            await Drain(view.Dispatcher);
            Require(view.LoadError == "不是有效的 GLB（magic 不符）。" && description.Text == view.LoadError,
                "The original GLB parse error must be visible inline without a nested message loop.");
            Require(!HasScene(view) && viewport.Children.Count == 0 && File.ReadAllBytes(bad).SequenceEqual(badBytes),
                "An invalid source must remain unchanged and leave no stale model.");

            string ply = Path.Combine(work, "stitched.ply");
            byte[] plyBytes = Encoding.ASCII.GetBytes("ply\nformat ascii 1.0\nelement vertex 1\nproperty float x\nproperty float y\nproperty float z\nend_header\n0 0 0\n");
            File.WriteAllBytes(ply, plyBytes);
            using (var lockedPly = new FileStream(ply, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                using (view.Dispatcher.DisableProcessing()) view.GlbPath = ply;
                await Drain(view.Dispatcher);
                Require(view.LoadError?.Contains("不支援的模型格式") == true && !HasScene(view),
                    "PLY input must be rejected by extension before the GLB parser or any file read.");
            }
            Require(File.ReadAllBytes(ply).SequenceEqual(plyBytes), "Rejecting PLY changed its measured source bytes.");

            string empty = Path.Combine(work, "empty.glb");
            byte[] emptyBytes = BuildGlb(empty: true);
            File.WriteAllBytes(empty, emptyBytes);
            using (view.Dispatcher.DisableProcessing()) view.GlbPath = empty;
            await Drain(view.Dispatcher);
            Require(view.LoadError?.Contains("沒有可顯示的頂點") == true && !HasScene(view) && viewport.Children.Count == 0,
                "An empty API-coordinate mesh must become an inline data error before invalid camera values reach rendering.");
            Require(File.ReadAllBytes(empty).SequenceEqual(emptyBytes), "Rejecting an empty mesh changed its source bytes.");

            string overflow = Path.Combine(work, "overflowing-bounds.glb");
            byte[] overflowBytes = BuildGlb(overflowingBounds: true);
            File.WriteAllBytes(overflow, overflowBytes);
            using (view.Dispatcher.DisableProcessing()) view.GlbPath = overflow;
            await Drain(view.Dispatcher);
            Require(view.LoadError?.Contains("超出範圍的座標") == true && !HasScene(view) && viewport.Children.Count == 0,
                "Nonfinite bounds must become an inline data error before rendering.");
            Require(File.ReadAllBytes(overflow).SequenceEqual(overflowBytes), "Rejecting nonfinite bounds changed source bytes.");

            string valid = Path.Combine(work, "triangle.glb");
            byte[] validBytes = BuildGlb();
            File.WriteAllBytes(valid, validBytes);
            using (view.Dispatcher.DisableProcessing()) view.GlbPath = valid;
            await Drain(view.Dispatcher);
            AssertTriangle(view);
            Require(view.LoadError is null && File.ReadAllBytes(valid).SequenceEqual(validBytes),
                "A valid GLB must recover from a previous error without rewriting its source.");

            // Failed loads are not retried by layout. Replacing the bytes alone
            // and resizing must keep the recorded error; an explicit retry works.
            string retry = Path.Combine(work, "retry.glb");
            File.WriteAllBytes(retry, badBytes);
            view.GlbPath = retry;
            await Drain(view.Dispatcher);
            string? recordedError = view.LoadError;
            Require(recordedError is not null, "The retry fixture did not reach the failure state.");
            File.WriteAllBytes(retry, validBytes);
            for (int i = 0; i < 3; i++)
            {
                view.Measure(new Size(640 + i * 80, 400));
                view.Arrange(new Rect(0, 0, 640 + i * 80, 400));
                view.UpdateLayout();
                await Drain(view.Dispatcher);
            }
            Require(!HasScene(view) && view.LoadError == recordedError,
                "Resizing must not repeatedly load a model whose last explicit attempt failed.");
            view.LoadPath(retry);
            await Drain(view.Dispatcher);
            AssertTriangle(view);
            Require(view.LoadError is null && File.ReadAllBytes(retry).SequenceEqual(validBytes),
                "An explicit same-path retry must recover and clear the inline error.");

            using (view.Dispatcher.DisableProcessing())
            {
                view.LoadPath(bad);
                view.LoadPath(valid);
            }
            await Drain(view.Dispatcher);
            AssertTriangle(view);
            Require(view.LoadError is null, "An older queued bad path overwrote the latest valid path.");

            var annotations = typeof(GlbMeshViewport).GetField("_annotations", PrivateInstance)!.GetValue(view)!;
            annotations.GetType().GetMethod("ShowAt")!.Invoke(annotations, [new Point3D(1, 2, 0)]);
            await Drain(view.Dispatcher);
            Require(overlay.Children.Count > 0, "The clear-state fixture did not create a picked-point annotation.");
            using (view.Dispatcher.DisableProcessing())
            {
                view.LoadPath(bad);
                view.GlbPath = null;
            }
            await Drain(view.Dispatcher);
            Require(view.LoadError is null && !HasScene(view) && viewport.Children.Count == 0 && overlay.Children.Count == 0
                && !(bool)typeof(GlbMeshViewport).GetField("_autoLookHeadPending", PrivateInstance)!.GetValue(view)!,
                "A latest null path must cancel a queued load and clear model, annotations, error and pending head camera.");
            Require(description.Text.Contains("掃描完成後"), "Clearing the path must restore the normal empty-state description.");
            Console.WriteLine("PASS GLB dispatcher-safe loading, inline errors, format and mesh-data rejection, resize stability, recovery and latest-path clearing");
        }
        finally
        {
            string resolved = Path.GetFullPath(work);
            if (!resolved.StartsWith(Path.GetFullPath(parent) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || !Guid.TryParseExact(Path.GetFileName(resolved), "N", out _))
                throw new IOException("Refusing to remove an unexpected GLB regression directory.");
            Directory.Delete(resolved, recursive: true);
        }
    }

    private static bool HasScene(GlbMeshViewport view) =>
        (bool)typeof(GlbMeshViewport).GetField("_hasOrbitScene", PrivateInstance)!.GetValue(view)!;

    private static void AssertTriangle(GlbMeshViewport view)
    {
        var model = (GeometryModel3D?)typeof(GlbMeshViewport).GetField("_meshModel", PrivateInstance)!.GetValue(view);
        Require(HasScene(view) && model?.Geometry is MeshGeometry3D { Positions.Count: 3, TriangleIndices.Count: 3 },
            "The latest valid GLB must retain its actual three-vertex, one-triangle model.");
    }

    private static async Task Drain(Dispatcher dispatcher) =>
        await dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static byte[] BuildGlb(bool empty = false, bool overflowingBounds = false)
    {
        using var binary = new MemoryStream();
        using (var writer = new BinaryWriter(binary, Encoding.UTF8, leaveOpen: true))
        {
            foreach (float value in new float[] { 0, 0, 0, 10, 0, 0, 0, 10, 0 }) writer.Write(value);
            for (int i = 0; i < 3; i++) { writer.Write(0f); writer.Write(0f); writer.Write(1f); }
            foreach (uint index in new uint[] { 0, 1, 2 }) writer.Write(index);
        }
        var root = new
        {
            asset = new { version = "2.0" },
            extras = empty ? new { scanMillimetresToGltfMetres = "(x,y,z) -> (0.001*x,0.001*z,-0.001*y)" } : null,
            buffers = new[] { new { byteLength = (int)binary.Length } },
            bufferViews = new[] { new { buffer = 0, byteOffset = 0, byteLength = 36 }, new { buffer = 0, byteOffset = 36, byteLength = 36 }, new { buffer = 0, byteOffset = 72, byteLength = 12 } },
            accessors = new object[]
            {
                new { bufferView = 0, componentType = 5126, count = empty ? 0 : 3, type = "VEC3", min = overflowingBounds ? new[] { -1e308, 0d, 0d } : new[] { 0d, 0d, 0d }, max = overflowingBounds ? new[] { 1e308, 10d, 0d } : new[] { 10d, 10d, 0d } },
                new { bufferView = 1, componentType = 5126, count = empty ? 0 : 3, type = "VEC3" },
                new { bufferView = 2, componentType = 5125, count = empty ? 0 : 3, type = "SCALAR" },
            },
            meshes = new[] { new { primitives = new[] { new { attributes = new { POSITION = 0, NORMAL = 1 }, indices = 2 } } } },
        };
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(root);
        int jsonLength = (json.Length + 3) & ~3;
        using var output = new MemoryStream();
        using var result = new BinaryWriter(output, Encoding.UTF8, leaveOpen: true);
        result.Write(0x46546c67u); result.Write(2u); result.Write((uint)(12 + 8 + jsonLength + 8 + binary.Length));
        result.Write((uint)jsonLength); result.Write(0x4e4f534au); result.Write(json);
        for (int i = json.Length; i < jsonLength; i++) result.Write((byte)' ');
        result.Write((uint)binary.Length); result.Write(0x004e4942u); result.Write(binary.ToArray());
        return output.ToArray();
    }
}
