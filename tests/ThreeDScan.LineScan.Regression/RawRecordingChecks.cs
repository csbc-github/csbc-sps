using System.IO;
using System.Text;
using ThreeDScan.Infrastructure.LineScan.Raw;

namespace ThreeDScan.LineScan.Regression;

internal static class RawRecordingChecks
{
    internal static void Run()
    {
        string directory = Path.Combine(Path.GetTempPath(), "ThreeDScanRawReader_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "source.ply");
        try
        {
            void Write(bool complete = true, int? declared = null, bool gap = false, bool duplicateRay = false,
                bool inconsistent = false, bool wrap = false)
            {
                using var stream = File.Create(path);
                string header = "ply\nformat binary_little_endian 1.0\ncomment geometry 2D_scan_plane_Z_equals_0\n"
                    + $"comment recording_complete {(complete ? 1 : 0)}\nelement vertex {declared ?? (complete ? 60 : 0)}\n"
                    + "property float x\nproperty float y\nproperty float z\nproperty uint frame_id\n"
                    + "property uint timestamp_us\nproperty ushort scan_counter\nproperty ushort point_index\n"
                    + "property double elapsed_s\nend_header\n";
                stream.Write(Encoding.ASCII.GetBytes(header));
                using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
                for (uint frame = 0; frame < 20; frame++)
                for (ushort ray = 0; ray < 3; ray++)
                {
                    writer.Write((float)(100 + ray + frame)); writer.Write(500f); writer.Write(0f);
                    writer.Write(frame);
                    writer.Write(unchecked((wrap ? uint.MaxValue - 500u : 1000u) + frame * 1000
                        + (inconsistent && frame == 1 && ray == 1 ? 1u : 0u)));
                    writer.Write(unchecked((ushort)((wrap ? 65530u : 0u) + frame + (gap && frame >= 5 ? 1u : 0u))));
                    writer.Write(duplicateRay && frame == 1 && ray == 1 ? (ushort)0 : ray);
                    writer.Write(frame * .001);
                }
            }
            void Rejected(Action action, string name)
            {
                bool rejected = false;
                try { action(); } catch (InvalidDataException) { rejected = true; }
                Program.Check(rejected, name);
            }
            Write();
            byte[] original = File.ReadAllBytes(path);
            var block = RawProfileRecordingReader.ReadFirstBlock(path);
            Program.Check(block.RecordingComplete && block.Frames.Count == 20 && block.PayloadVertices == 60,
                "raw reader selects 20 real consecutive frames and records completion provenance");
            Program.Check(block.Frames[0].Points[0].X == 100 && block.Frames[^1].Points[^1].X == 121,
                "raw reader preserves native XY without calibration or reconstruction");
            Program.Check(original.SequenceEqual(File.ReadAllBytes(path)), "raw analysis never modifies its source recording");
            Write(complete: false);
            Rejected(() => RawProfileRecordingReader.ReadFirstBlock(path), "incomplete recording is rejected by default");
            Program.Check(!RawProfileRecordingReader.ReadFirstBlock(path, allowIncomplete: true).RecordingComplete,
                "explicit incomplete-file analysis never claims a complete capture");
            Write(declared: 59);
            Rejected(() => RawProfileRecordingReader.ReadFirstBlock(path), "complete raw payload count must match its header");
            Write(declared: -1);
            Rejected(() => RawProfileRecordingReader.ReadFirstBlock(path), "negative source vertex counts are rejected");
            Write();
            using (var truncated = new FileStream(path, FileMode.Open, FileAccess.Write)) truncated.SetLength(truncated.Length - 1);
            Rejected(() => RawProfileRecordingReader.ReadFirstBlock(path), "a partial last source vertex is rejected");
            Write(gap: true);
            Rejected(() => RawProfileRecordingReader.ReadFirstBlock(path), "raw analysis never averages across a missing scan");
            Write(duplicateRay: true);
            Rejected(() => RawProfileRecordingReader.ReadFirstBlock(path), "duplicate angle within a frame is rejected");
            Write(inconsistent: true);
            Rejected(() => RawProfileRecordingReader.ReadFirstBlock(path), "inconsistent timestamps inside one frame are rejected");
            Write(wrap: true);
            Program.Check(RawProfileRecordingReader.ReadFirstBlock(path).Frames.Count == 20,
                "genuine scanner timestamp and counter wraps retain consecutive source frames");
            using var canceled = new CancellationTokenSource(); canceled.Cancel();
            bool interrupted = false;
            try { RawProfileRecordingReader.ReadFirstBlock(path, cancellationToken: canceled.Token); }
            catch (OperationCanceledException) { interrupted = true; }
            Program.Check(interrupted, "raw recording analysis honors cancellation before reading");
        }
        finally
        {
            string resolved = Path.GetFullPath(directory);
            string tempParent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar);
            if (!string.Equals(Path.GetDirectoryName(resolved), tempParent, StringComparison.OrdinalIgnoreCase)
                || !Path.GetFileName(resolved).StartsWith("ThreeDScanRawReader_", StringComparison.Ordinal))
                throw new InvalidOperationException("Refusing to remove a path outside the owned test directory.");
            Directory.Delete(resolved, recursive: true);
        }
        Console.WriteLine("PASS raw recording reader: source provenance, native XY, incomplete-file opt-in and continuity guards");
    }
}
