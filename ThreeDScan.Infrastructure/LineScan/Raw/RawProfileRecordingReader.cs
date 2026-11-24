using System.Text;
using ThreeDScan.Infrastructure.LineScan.Hank;

namespace ThreeDScan.Infrastructure.LineScan.Raw;

public sealed record RawProfileFrame(uint FrameId, uint Timestamp, ushort Counter, IReadOnlyList<Vertex> Points);

public sealed record RawProfileBlock(string SourcePath, bool RecordingComplete,
    IReadOnlyList<RawProfileFrame> Frames, long PayloadVertices);

/// <summary>Reads original scanner XY for explicit offline analysis, without reconstruction or calibration.</summary>
public static class RawProfileRecordingReader
{
    public static RawProfileBlock ReadFirstBlock(string path, int frameCount = 20,
        bool allowIncomplete = false, CancellationToken cancellationToken = default)
    {
        if (frameCount is < 1 or > 600) throw new ArgumentOutOfRangeException(nameof(frameCount));
        cancellationToken.ThrowIfCancellationRequested();
        string source = Path.GetFullPath(path);
        using var stream = File.OpenRead(source);
        var header = new List<string>();
        while (true)
        {
            var bytes = new List<byte>();
            while (true)
            {
                int value = stream.ReadByte();
                if (value < 0 || stream.Position > 65536 || bytes.Count > 4096)
                    throw new InvalidDataException("原始 PLY 表頭不完整或過大。");
                if (value == 10) break;
                if (value > 127) throw new InvalidDataException("原始 PLY 表頭必須是 ASCII。");
                bytes.Add((byte)value);
            }
            string line = Encoding.ASCII.GetString(bytes.ToArray()).TrimEnd('\r');
            header.Add(line);
            if (line == "end_header") break;
        }

        string[] properties = ["property float x", "property float y", "property float z",
            "property uint frame_id", "property uint timestamp_us", "property ushort scan_counter",
            "property ushort point_index", "property double elapsed_s"];
        if (header.Count < 3 || header[0] != "ply" || header[1] != "format binary_little_endian 1.0"
            || !header.Contains("comment geometry 2D_scan_plane_Z_equals_0")
            || !header.Where(line => line.StartsWith("property ", StringComparison.Ordinal)).SequenceEqual(properties))
            throw new InvalidDataException("請使用本專案保存的原始 2D 掃描 PLY。");
        var elements = header.Where(line => line.StartsWith("element ", StringComparison.Ordinal)).ToArray();
        if (elements.Length != 1 || !elements[0].StartsWith("element vertex ", StringComparison.Ordinal)
            || !long.TryParse(elements[0].Split(' ', StringSplitOptions.RemoveEmptyEntries)[^1], out long declared)
            || declared < 0)
            throw new InvalidDataException("原始 PLY 點數宣告無效。");
        var completion = header.Where(line => line.StartsWith("comment recording_complete ", StringComparison.Ordinal)).ToArray();
        if (completion.Length != 1 || completion[0] is not ("comment recording_complete 0" or "comment recording_complete 1"))
            throw new InvalidDataException("原始 PLY 缺少有效的錄製完成標記。");
        bool complete = completion[0].EndsWith('1');
        if (!complete && !allowIncomplete)
            throw new InvalidDataException("此錄製未完成；僅明確允許不完整錄製的離線診斷可讀取，不能當作正式量測結果。");
        long payloadBytes = stream.Length - stream.Position;
        if (payloadBytes % 32 != 0 || complete && payloadBytes / 32 != declared)
            throw new InvalidDataException("原始 PLY 點數或資料長度不符。");

        using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);
        var frames = new List<RawProfileFrame>();
        var points = new List<Vertex>();
        var rays = new HashSet<ushort>();
        uint? currentFrame = null;
        uint? previousFrameId = null;
        void CommitFrame()
        {
            if (points.Count == 0) return;
            var first = points[0];
            if (frames.Count > 0)
            {
                var previous = frames[^1];
                int delta = (first.Counter - previous.Counter) & 65535;
                uint elapsed = unchecked(first.Timestamp - previous.Timestamp);
                if (delta == 0 && elapsed == 0)
                {
                    if (points.Count != previous.Points.Count || points.Zip(previous.Points).Any(pair =>
                        pair.First.Ray != pair.Second.Ray || pair.First.X != pair.Second.X
                        || pair.First.Y != pair.Second.Y || pair.First.Z != pair.Second.Z))
                        throw new InvalidDataException("重複掃描封包含有衝突座標。");
                    return;
                }
                if (delta != 1 || elapsed == 0 || elapsed >= 0x80000000)
                    throw new InvalidDataException("原始掃描圈數或時間不連續，不能補造平均樣本。");
            }
            frames.Add(new(first.Frame, first.Timestamp, first.Counter, Array.AsReadOnly(points.ToArray())));
        }

        for (long index = 0; index < payloadBytes / 32; index++)
        {
            if ((index & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
            var point = new Vertex(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(),
                reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadUInt16(), reader.ReadUInt16(), reader.ReadDouble());
            if (currentFrame != point.Frame)
            {
                CommitFrame();
                if (frames.Count == frameCount) break;
                if (previousFrameId.HasValue && point.Frame <= previousFrameId.Value)
                    throw new InvalidDataException("原始掃描影格順序異常。");
                currentFrame = point.Frame;
                previousFrameId = point.Frame;
                points.Clear();
                rays.Clear();
            }
            if (!double.IsFinite(point.X) || !double.IsFinite(point.Y) || point.Z != 0
                || !double.IsFinite(point.Elapsed) || point.Elapsed < 0)
                throw new InvalidDataException("原始點包含無效座標或時間。");
            if (points.Count >= 16384 || !rays.Add(point.Ray))
                throw new InvalidDataException("原始單圈點數過大或掃描角度重複。");
            if (points.Count > 0 && (point.Timestamp != points[0].Timestamp || point.Counter != points[0].Counter
                || point.Elapsed != points[0].Elapsed))
                throw new InvalidDataException("同一原始掃描圈的時間或計數器不一致。");
            points.Add(point);
        }
        if (frames.Count < frameCount) CommitFrame();
        if (frames.Count != frameCount)
            throw new InvalidDataException($"有效連續原始圈數不足 {frameCount} 圈。");
        return new(source, complete, frames.AsReadOnly(), payloadBytes / 32);
    }
}
