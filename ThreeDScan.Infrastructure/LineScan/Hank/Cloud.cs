using System.Globalization;
using System.Text;
using System.Text.Json;
namespace ThreeDScan.Infrastructure.LineScan.Hank;

public record Vertex(double X, double Y, double Z, uint Frame, uint Timestamp, ushort Counter, ushort Ray, double Elapsed)
{
    public P XY => new(X, Y);
}

public record Cloud(List<Vertex> Points, bool Raw, int AverageScans = 1,
    string PositionMethod = "constant_speed_estimate_from_user_travel", bool Simulation = false, string? SourcePath = null)
{
    public long? DeclaredFrames { get; init; }
    public string[] HeaderComments { get; init; } = [];
    public Dictionary<string, object?>? ReconstructionReport { get; init; }
    public Dictionary<string, object?>? AlignmentReport { get; init; }
    public bool PreviewOnly { get; init; }
    public Cloud FullData() => PreviewOnly ? Read(SourcePath ?? throw new InvalidOperationException("預覽資料缺少原始檔路徑")) : this;
    public long FrameIdOffset { get; init; }
    public bool LengthEstimated => PositionMethod != "encoder_roll_travel";

    public object PartReport(int id)
    {
        RequirePoints();
        return new { part_id = id, point_count = Points.Count, scan_count = Points.Select(p => p.Frame).Distinct().Count(),
            length_mm = Points.Max(p => p.Y) - Points.Min(p => p.Y), width_mm = Points.Max(p => p.X) - Points.Min(p => p.X),
            height_mm = Points.Max(p => p.Z), height_span_mm = Points.Max(p => p.Z) - Points.Min(p => p.Z),
            min_xyz_mm = Minimum(), max_xyz_mm = Maximum(), length_estimated = LengthEstimated };
    }

    public object Dimensions()
    {
        if (PreviewOnly && ReconstructionReport?.GetValueOrDefault("dimensions") is object dimensions) return dimensions;
        RequirePoints();
        double height = Points.Max(p => p.Z) - Points.Min(p => p.Z);
        string lengthNote = LengthEstimated ? "行程與時間估算" : "Encoder 計數與滾輪半徑換算";
        return new { units = "mm", method = "visible_surface_axis_aligned_bounds", points = Points.Count,
            width_mm = Points.Max(p => p.X) - Points.Min(p => p.X), length_mm = Points.Max(p => p.Y) - Points.Min(p => p.Y),
            height_mm = height, top_above_baseline_mm = Points.Max(p => p.Z),
            min_xyz_mm = Minimum(), max_xyz_mm = Maximum(), length_estimated = LengthEstimated,
            position_method = PositionMethod,
            note = $"可見點雲範圍：長=Y跨度（{lengthNote}）、寬=X跨度、高=Z跨度。裁切、缺面及離群點會影響結果；不是角鋼腿長或厚度。" };
    }

    void RequirePoints() { if (Points.Count == 0) throw new ArgumentException("沒有有效點"); }
    double[] Minimum() => [Points.Min(p => p.X), Points.Min(p => p.Y), Points.Min(p => p.Z)];
    double[] Maximum() => [Points.Max(p => p.X), Points.Max(p => p.Y), Points.Max(p => p.Z)];

    public List<Cloud> Split(double gap = 10)
    {
        if (!double.IsFinite(gap) || gap <= 0) throw new ArgumentException("分件間距需大於零");
        var list = new List<Cloud>();
        foreach (var p in Points.OrderBy(v => v.X))
        {
            if (list.Count == 0 || p.X - list[^1].Points[^1].X > gap) list.Add(this with { Points = [], ReconstructionReport = null });
            list[^1].Points.Add(p);
        }
        return list;
    }

    public Cloud Align(double gap = 10)
    {
        if (PreviewOnly) return FullData().Align(gap);
        RequirePoints();
        var aligned = new List<List<Vertex>>();
        var shiftsByPoint = new Dictionary<Vertex, double>();
        var reports = new List<object>();
        foreach (var part in Split(gap))
        {
            var frames = part.Points.GroupBy(p => p.Frame).OrderBy(g => g.Key).ToList();
            double reference = frames[0].Min(p => p.X);
            var moved = new List<Vertex>();
            var shifts = new List<(uint Frame, double Left, double Shift)>();
            foreach (var frame in frames)
            {
                double shift = reference - frame.Min(p => p.X);
                moved.AddRange(frame.Select(p => p with { X = p.X + shift }));
                foreach (var point in frame) shiftsByPoint[point] = shift;
                shifts.Add((frame.Key, frame.Min(p => p.X), shift));
            }
            aligned.Add(moved);
            reports.Add(new { part = reports.Count + 1, reference_frame_id = (long)frames[0].Key - FrameIdOffset, target_left_x_mm = reference,
                frames = frames.Count, points = part.Points.Count, shifts = shifts.Select(s => new { frame_id = (long)s.Frame - FrameIdOffset, source_left_x_mm = s.Left, shift_x_mm = s.Shift }).ToArray(),
                max_abs_shift_x_mm = shifts.Max(s => Math.Abs(s.Shift)) });
        }
        for (int i = 1; i < aligned.Count; i++)
            if (aligned[i - 1].Max(p => p.X) + gap >= aligned[i].Min(p => p.X))
                throw new ArgumentException("靠左後兩件的 X 範圍重疊或間距不足，請分別框選重建後校正");
        return this with { Points = Points.Select(p => p with { X = p.X + shiftsByPoint[p] }).ToList(),
            HeaderComments = HeaderComments.Append("left_alignment per_part_per_frame_X_only_first_frame").Distinct().ToArray(), ReconstructionReport = null,
            AlignmentReport = new() { ["method"] = "per_part_per_frame_left_x_to_first_frame", ["gap_mm"] = gap, ["part_count"] = reports.Count,
                ["parts"] = reports, ["axes_changed"] = new[] { "x" }, ["baseline_preserved"] = true,
                ["note"] = "每件每圈僅沿 X 平移至該件首圈左端；Y、Z、桌面底線與圈內形狀保持不變。" } };
    }

    public static Cloud Read(string file)
    {
        using var stream = File.OpenRead(file);
        string ReadLine()
        {
            var bytes = new List<byte>();
            while (true)
            {
                int value = stream.ReadByte();
                if (value < 0 || bytes.Count > 4096 || stream.Position > 65536) throw new InvalidDataException("PLY 表頭無效");
                if (value == 10) return Encoding.ASCII.GetString(bytes.ToArray()).TrimEnd('\r');
                if (value > 127) throw new InvalidDataException("PLY 表頭必須是 ASCII");
                bytes.Add((byte)value);
            }
        }
        var lines = new List<string>();
        while (stream.Position < 65536) { var line = ReadLine(); lines.Add(line); if (line == "end_header") break; }
        if (lines.Count < 3 || lines[0] != "ply" || lines[1] != "format binary_little_endian 1.0" || lines[^1] != "end_header" ||
            !lines.Contains("comment recording_complete 1")) throw new InvalidDataException("需完整錄製的 SICK binary PLY");
        string[] properties = ["property float x", "property float y", "property float z", "property uint frame_id", "property uint timestamp_us", "property ushort scan_counter", "property ushort point_index", "property double elapsed_s"];
        if (!lines.Where(l => l.StartsWith("property ")).SequenceEqual(properties) || lines.Count(l => l.StartsWith("element ")) != 1) throw new InvalidDataException("PLY 欄位不符");
        var vertexLine = lines.SingleOrDefault(l => l.StartsWith("element vertex "));
        if (vertexLine == null || !long.TryParse(vertexLine.Split(' ')[^1], out long count) || count < 0 || count > int.MaxValue || stream.Length - stream.Position != count * 32) throw new InvalidDataException("PLY 點數不符");
        bool raw = lines.Contains("comment geometry 2D_scan_plane_Z_equals_0");
        bool encoder = lines.Contains("comment geometry 3D_encoder_roll_travel");
        if (!raw && !encoder && !lines.Contains("comment geometry 3D_manual_translation_estimated")) throw new InvalidDataException("非本專案幾何");
        using var reader = new BinaryReader(stream);
        var points = new List<Vertex>((int)count);
        for (int i = 0; i < count; i++)
        {
            var p = new Vertex(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadUInt16(), reader.ReadUInt16(), reader.ReadDouble());
            if (!double.IsFinite(p.X) || !double.IsFinite(p.Y) || !double.IsFinite(p.Z) || !double.IsFinite(p.Elapsed)) throw new InvalidDataException("非有限座標");
            points.Add(p);
        }
        int average = 1;
        var averageLine = lines.FirstOrDefault(l => l.StartsWith("comment average_scans "));
        if (averageLine != null && !int.TryParse(averageLine.Split(' ')[^1], out average)) throw new InvalidDataException("PLY 平均圈數無效");
        if (average < 1 || average > 600) throw new InvalidDataException("PLY 平均圈數無效");
        long? declared = null;
        var frameLine = lines.FirstOrDefault(l => l.StartsWith("comment frames_written "));
        if (frameLine != null)
        {
            if (!long.TryParse(frameLine.Split(' ')[^1], out long frames) || frames < 0) throw new InvalidDataException("PLY 影格數無效");
            declared = frames;
        }
        return new(points, raw, average, encoder ? "encoder_roll_travel" : "constant_speed_estimate_from_user_travel",
            lines.Contains("comment encoder_source synthetic"), Path.GetFullPath(file))
        { DeclaredFrames = declared, HeaderComments = lines.Where(l => l.StartsWith("comment ")).Select(l => l[8..]).ToArray() };
    }

    public void Write(string path)
    {
        if (PreviewOnly) { File.Copy(SourcePath ?? throw new InvalidOperationException("預覽資料缺少原始檔路徑"), path, overwrite: false); return; }
        using var output = new PlyWriter(path, Raw, AverageScans, PositionMethod, Simulation, HeaderComments, FrameIdOffset);
        foreach (var p in Points) output.Add(p);
        output.Complete(Raw ? DeclaredFrames : null);
    }

    internal List<List<Vertex>> RawFrames()
    {
        var frames = new List<List<Vertex>>();
        foreach (var point in Points)
        {
            if (frames.Count == 0 || frames[^1][0].Frame != point.Frame)
            {
                if (frames.Count > 0 && point.Frame <= frames[^1][0].Frame) throw new InvalidDataException("原始影格順序異常");
                frames.Add([]);
            }
            var frame = frames[^1];
            if (frame.Count > 0 && (point.Timestamp != frame[0].Timestamp || point.Counter != frame[0].Counter)) throw new InvalidDataException("同一圈的時間或計數器不一致");
            frame.Add(point);
            if (frame.Count > 16384) throw new InvalidDataException("單圈點數過大");
        }
        return frames;
    }

    static List<MotionFrame> TimedFrames(List<List<Vertex>> raw)
    {
        var frames = new List<MotionFrame>(); Vertex? previous = null; ulong microseconds = 0;
        foreach (var frame in raw)
        {
            var first = frame[0];
            if (previous != null)
            {
                if (first.Timestamp == previous.Timestamp && first.Counter == previous.Counter) continue;
                uint delta = unchecked(first.Timestamp - previous.Timestamp);
                if (delta == 0 || delta >= 0x80000000 || first.Counter == previous.Counter) throw new InvalidDataException("時間／計數器不連續，請分段錄製");
                microseconds = checked(microseconds + delta);
            }
            frames.Add(new(microseconds / 1e6, null, frame)); previous = first;
        }
        return frames;
    }

    public Cloud Reconstruct(Calibration calibration, double travel, int average = 1, double start = 0, double? end = null,
        double minHeight = 3, double maxHeight = 500, double xmin = -1000, double xmax = 1000, int direction = 1,
        double[]? roi = null, string positionSource = "time", double wheelRadiusMm = 100, int encoderCountsPerRev = 1024, int encoderChannel = 1)
    {
        if (PreviewOnly) throw new InvalidOperationException("請以串流重建完整原始錄製檔");
        if (!Raw) throw new ArgumentException("需原始 2D PLY"); calibration.Validate();
        double scale = 2 * Math.PI * wheelRadiusMm / encoderCountsPerRev;
        if (new[] { travel, start, minHeight, maxHeight, xmin, xmax, wheelRadiusMm, scale }.Any(v => !double.IsFinite(v)) ||
            travel <= 0 || average < 1 || average > 600 || start < 0 || direction != 1 && direction != -1 || minHeight >= maxHeight || xmin >= xmax ||
            positionSource is not ("time" or "encoder") || wheelRadiusMm <= 0 || encoderCountsPerRev <= 0 || scale <= 0 || encoderChannel < 1 || encoderChannel > 16 ||
            end.HasValue && (!double.IsFinite(end.Value) || end.Value <= start) ||
            roi != null && (roi.Length != 4 || roi.Any(v => !double.IsFinite(v)) || roi[0] >= roi[1] || roi[2] >= roi[3])) throw new ArgumentException("重建設定無效");
        JsonElement? capture = null;
        if (SourcePath != null && File.Exists(Path.ChangeExtension(SourcePath, ".capture.json")))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(Path.ChangeExtension(SourcePath, ".capture.json")));
            capture = document.RootElement.Clone();
            if (capture.Value.ValueKind != JsonValueKind.Object) throw new InvalidDataException("錄製設定檔必須是 JSON 物件");
            if (capture.Value.TryGetProperty("error", out var error) && error.ValueKind != JsonValueKind.Null &&
                !(error.ValueKind == JsonValueKind.String && string.IsNullOrEmpty(error.GetString())) && error.ValueKind != JsonValueKind.False)
                throw new InvalidDataException("錄製有異常，請重新錄製：" + error);
        }
        bool encoder = positionSource == "encoder";
        var raw = RawFrames();
        EncoderMotion? motion = encoder ? EncoderMotion.Read(this, raw, encoderChannel) : null;
        var frames = motion?.Frames ?? TimedFrames(raw);
        double duration = frames.Count > 0 ? frames[^1].Time : 0, stop = end ?? duration;
        if (frames.Count(f => f.Points.Count > 0) < 2 || stop <= start || stop > duration + 1e-9) throw new ArgumentException("時間範圍無效或不足兩個有效截面");
        var selected = frames.Where(f => f.Time >= start && f.Time <= stop).ToList();
        double origin = encoder && selected.Count > 0 ? selected[0].Position!.Value : 0;
        if (encoder && (selected.Count == 0 || selected.Min(f => f.Position) == selected.Max(f => f.Position))) throw new ArgumentException("Encoder 在選取範圍沒有位置變化；請確認接線、資料輸出及測輪轉動");
        var result = new List<Vertex>();
        for (int k = 0; k < selected.Count;)
        {
            var initial = selected[k++]; if (initial.Points.Count == 0) continue;
            var block = new List<MotionFrame> { initial };
            while (block.Count < average && k < selected.Count)
            {
                var a = block[^1].Points[0]; var next = selected[k]; if (next.Points.Count == 0) break; var b = next.Points[0];
                // Encoder deduplication preserves source IDs, so only the device
                // counter determines continuity between distinct encoder scans.
                if (((b.Counter - a.Counter) & 65535) != 1 || !encoder && b.Frame != a.Frame + 1) break;
                block.Add(selected[k++]);
            }
            var first = initial.Points[0]; double time = block.Average(b => b.Time); List<Vertex> outputFrame;
            if (average == 1)
            {
                outputFrame = initial.Points;
            }
            else
            {
                var mean = new PointMean();
                foreach (var b in block) { var p = new P?[b.Points.Max(v => (int)v.Ray) + 1]; foreach (var v in b.Points) p[v.Ray] = v.XY; mean.Add(p); }
                var points = mean.Result(); outputFrame = [];
                for (int ray = 0; ray < points.Length; ray++) if (points[ray] is P p)
                    outputFrame.Add(new(p.X, p.Y, 0, first.Frame, unchecked(first.Timestamp + (uint)Math.Round((time - initial.Time) * 1e6)), first.Counter, (ushort)ray, first.Elapsed + time - initial.Time));
            }
            double y = encoder ? direction * (block.Average(b => b.Position!.Value) - origin) * scale : direction * travel * (time - start) / (stop - start);
            foreach (var vertex in outputFrame)
            {
                var p = vertex.XY;
                if (roi != null && (p.X < roi[0] || p.X > roi[1] || p.Y < roi[2] || p.Y > roi[3])) continue;
                var v = calibration.Transform(p); if (v.X < xmin || v.X > xmax || v.Y < minHeight || v.Y > maxHeight) continue;
                result.Add(vertex with { X = v.X, Y = y, Z = v.Y });
            }
        }
        int outputFrames = result.Select(p => p.Frame).Distinct().Count();
        if (outputFrames < 2) throw new ArgumentException("有效截面不足兩圈，請減少平均圈數或檢查校正、範圍與裁切時間");
        string method = encoder ? "encoder_roll_travel" : "constant_speed_estimate_from_user_travel";
        bool simulation = encoder && (motion!.Simulation || capture is JsonElement info && info.TryGetProperty("synthetic", out var synthetic) && synthetic.ValueKind == JsonValueKind.True);
        string[] comments = encoder ? ["Y_encoder_roll_travel", $"encoder_wheel_radius_mm {wheelRadiusMm.ToString("G17", CultureInfo.InvariantCulture)}", $"encoder_counts_per_rev {encoderCountsPerRev}", $"encoder_channel {encoderChannel}", $"encoder_mm_per_count {scale.ToString("G17", CultureInfo.InvariantCulture)}"] : ["Y_estimated_from_time_and_user_travel"];
        var rebuilt = new Cloud(result, false, average, method, simulation) { HeaderComments = comments, DeclaredFrames = outputFrames };
        var report = new Dictionary<string, object?>
        {
            ["source"] = SourcePath, ["units"] = "mm", ["position_method"] = method,
            ["warning"] = encoder ? "Y uses encoder wheel travel; effective radius and wheel slip affect its scale." : "Y is estimated, not measured. Uneven manual motion distorts geometry.",
            ["axes"] = new { x = "along fixed baseline", y = encoder ? "encoder wheel travel" : "estimated travel", z = "height toward sensor" },
            ["calibration"] = new { first = new[] { calibration.First.X, calibration.First.Y }, second = new[] { calibration.Second.X, calibration.Second.Y } },
            ["settings"] = new { travel_mm = travel, start_s = start, end_s = end, xmin, xmax, min_height = minHeight, max_height = maxHeight, direction, average_scans = average, roi_xy = roi, position_source = positionSource, wheel_radius_mm = wheelRadiusMm, encoder_counts_per_rev = encoderCountsPerRev, encoder_channel = encoderChannel },
            ["raw_duration_s"] = duration, ["effective_end_s"] = stop, ["raw_frames_with_points"] = frames.Count(f => f.Points.Count > 0),
            ["output_frames"] = outputFrames, ["output_points"] = result.Count, ["dimensions"] = rebuilt.Dimensions(), ["capture"] = capture,
            ["averaging"] = new { scans_per_block = average, method = "majority_range_cluster_mean_v2", minimum_support_fraction = .6, position = encoder ? "mean_encoder_distance" : "mean_device_time", edge_threshold_mm = 10, metadata = "first_source_frame_and_counter; mean_timestamp" }
        };
        if (encoder)
        {
            report["simulation"] = simulation;
            report["position_reference"] = new { sidecar = Path.ChangeExtension(SourcePath, ".encoder.jsonl"), wheel_radius_mm = wheelRadiusMm, encoder_counts_per_rev = encoderCountsPerRev,
                encoder_channel = encoderChannel, mm_per_count = scale, first_selected_unwrapped_count = origin,
                selected_count_range = new[] { selected.Min(f => f.Position!.Value), selected.Max(f => f.Position!.Value) }, source = simulation ? "synthetic" : "recorded", simulation };
        }
        return rebuilt with { ReconstructionReport = report };
    }
}

public sealed class PlyWriter : IDisposable
{
    readonly FileStream stream; readonly BinaryWriter writer; readonly string header; readonly long frameIdOffset; long count; readonly HashSet<uint> frames = []; readonly bool trackFrames;
    public PlyWriter(string path, bool raw, int average = 1, string positionMethod = "constant_speed_estimate_from_user_travel", bool simulation = false, IEnumerable<string>? comments = null, long frameIdOffset = 0, bool trackFrames = true)
    {
        if (frameIdOffset < 0 || frameIdOffset > 2147483648L || raw && frameIdOffset != 0) throw new ArgumentException("frame_id 位移無效");
        this.frameIdOffset = frameIdOffset; this.trackFrames = trackFrames;
        stream = new(path, FileMode.CreateNew, FileAccess.ReadWrite); writer = new(stream);
        bool encoder = !raw && positionMethod == "encoder_roll_travel";
        var preserved = (comments ?? []).Where(c => !c.StartsWith("geometry ") && !c.StartsWith("average_scans ") && !c.StartsWith("frames_written ") && !c.StartsWith("recording_complete ") && !c.StartsWith("position_method ") && !c.StartsWith("encoder_source ") && c != "units millimeters" && !c.Contains('\n') && !c.Contains('\r'));
        string extras = string.Concat(preserved.Distinct().Select(c => "comment " + c + "\n"));
        if (encoder) extras += "comment position_method encoder_roll_travel\ncomment encoder_source " + (simulation ? "synthetic" : "recorded") + "\n";
        else if (raw && simulation) extras += "comment encoder_source synthetic\n";
        header = $"ply\nformat binary_little_endian 1.0\ncomment units millimeters\ncomment geometry {(raw ? "2D_scan_plane_Z_equals_0" : encoder ? "3D_encoder_roll_travel" : "3D_manual_translation_estimated")}\ncomment average_scans {average}\ncomment frames_written 00000000000000000000\ncomment recording_complete 0\nelement vertex 00000000000000000000\nproperty float x\nproperty float y\nproperty float z\nproperty {(frameIdOffset == 0 ? "uint" : "int")} frame_id\nproperty uint timestamp_us\nproperty ushort scan_counter\nproperty ushort point_index\nproperty double elapsed_s\n{extras}end_header\n";
        writer.Write(Encoding.ASCII.GetBytes(header));
    }
    public void Add(Vertex p)
    {
        float x = (float)p.X, y = (float)p.Y, z = (float)p.Z;
        if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(z) || !double.IsFinite(p.Elapsed)) throw new OverflowException("點座標超出 PLY float 可表示範圍，或時間不是有限數值");
        int signedFrame = frameIdOffset == 0 ? 0 : checked((int)((long)p.Frame - frameIdOffset));
        writer.Write(x); writer.Write(y); writer.Write(z);
        if (frameIdOffset == 0) writer.Write(p.Frame); else writer.Write(signedFrame);
        writer.Write(p.Timestamp); writer.Write(p.Counter); writer.Write(p.Ray); writer.Write(p.Elapsed);
        count++; if (trackFrames) frames.Add(p.Frame);
    }
    public void Complete(long? recordedFrames = null)
    {
        if (recordedFrames < 0 || !trackFrames && recordedFrames == null || trackFrames && recordedFrames < frames.Count) throw new ArgumentException("錄製影格數小於已寫入影格數");
        void Patch(string key, string value) { stream.Position = header.IndexOf(key, StringComparison.Ordinal) + key.Length; writer.Write(Encoding.ASCII.GetBytes(value)); }
        Patch("element vertex ", count.ToString("D20", CultureInfo.InvariantCulture));
        Patch("comment frames_written ", (recordedFrames ?? frames.Count).ToString("D20", CultureInfo.InvariantCulture));
        Patch("comment recording_complete ", "1"); writer.Flush();
    }
    public void Dispose() { writer.Dispose(); stream.Dispose(); }
}
