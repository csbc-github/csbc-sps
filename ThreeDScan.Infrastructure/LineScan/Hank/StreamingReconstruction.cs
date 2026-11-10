using System.Globalization;
using System.Text;
using System.Text.Json;
namespace ThreeDScan.Infrastructure.LineScan.Hank;

// Keeps only the current raw frame, a <=600-frame averaging window and a <=12000-point preview.
public static class StreamingReconstruction
{
    sealed record RawHeader(long Vertices, long? Frames, string[] Comments);

    static RawHeader Header(Stream stream)
    {
        string ReadLine()
        {
            var bytes = new List<byte>();
            while (true)
            {
                int value = stream.ReadByte();
                if (value < 0 || bytes.Count >= 4096 || stream.Position > 65536) throw new InvalidDataException("PLY 表頭不完整或過大");
                if (value == 10) return Encoding.ASCII.GetString(bytes.ToArray());
                if (value > 127) throw new InvalidDataException("PLY 表頭必須是 ASCII");
                bytes.Add((byte)value);
            }
        }
        var lines = new List<string>();
        while (true) { string line = ReadLine(); lines.Add(line); if (line == "end_header") break; }
        string[] properties = ["property float x", "property float y", "property float z", "property uint frame_id", "property uint timestamp_us", "property ushort scan_counter", "property ushort point_index", "property double elapsed_s"];
        if (lines.Count < 3 || lines[0] != "ply" || lines[1] != "format binary_little_endian 1.0" || !lines.Contains("comment geometry 2D_scan_plane_Z_equals_0") ||
            !lines.Where(l => l.StartsWith("property ", StringComparison.Ordinal)).SequenceEqual(properties)) throw new InvalidDataException("請選本專案錄製的原始 2D PLY，不是重建後的 3D PLY");
        if (!lines.Contains("comment recording_complete 1")) throw new InvalidDataException("原始錄製未完整完成，請重新錄製");
        var elements = lines.Where(l => l.StartsWith("element ", StringComparison.Ordinal)).ToArray();
        if (elements.Length != 1 || !elements[0].StartsWith("element vertex ", StringComparison.Ordinal) ||
            !long.TryParse(elements[0].Split(' ')[^1], out long count) || count < 0 || count > long.MaxValue / 32 || stream.Length - stream.Position != count * 32)
            throw new InvalidDataException("PLY 點數與資料長度不符");
        long? frames = null;
        foreach (string line in lines.Where(l => l.StartsWith("comment frames_written ", StringComparison.Ordinal)))
        {
            if (!long.TryParse(line.Split(' ')[^1], out long value) || value < 0) throw new InvalidDataException("Raw PLY frame count is invalid");
            frames = value;
        }
        return new(count, frames, lines.Where(l => l.StartsWith("comment ", StringComparison.Ordinal)).Select(l => l[8..]).ToArray());
    }

    static IEnumerable<List<Vertex>> RawFrames(string source)
    {
        using var stream = File.OpenRead(source); var header = Header(stream); using var reader = new BinaryReader(stream);
        var frame = new List<Vertex>(); uint? frameId = null;
        for (long i = 0; i < header.Vertices; i++)
        {
            var p = new Vertex(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadUInt16(), reader.ReadUInt16(), reader.ReadDouble());
            if (!double.IsFinite(p.X) || !double.IsFinite(p.Y) || !double.IsFinite(p.Z) || !double.IsFinite(p.Elapsed)) throw new InvalidDataException("原始點雲包含非有限數值");
            if (p.Frame != frameId)
            {
                if (frame.Count > 0) yield return frame;
                if (frameId.HasValue && p.Frame <= frameId.Value) throw new InvalidDataException("原始影格順序異常");
                frame = []; frameId = p.Frame;
            }
            if (frame.Count > 0 && (p.Timestamp != frame[0].Timestamp || p.Counter != frame[0].Counter)) throw new InvalidDataException("同一圈的時間或計數器不一致");
            frame.Add(p);
            if (frame.Count > 16384) throw new InvalidDataException("單圈點數過大");
        }
        if (frame.Count > 0) yield return frame;
    }

    static IEnumerable<MotionFrame> TimedFrames(string source)
    {
        Vertex? previous = null; ulong elapsed = 0;
        foreach (var frame in RawFrames(source))
        {
            var first = frame[0];
            if (previous != null)
            {
                if (first.Timestamp == previous.Timestamp && first.Counter == previous.Counter) continue;
                uint delta = unchecked(first.Timestamp - previous.Timestamp);
                if (delta == 0 || delta >= 0x80000000 || first.Counter == previous.Counter) throw new InvalidDataException("時間／計數器不連續，可能重啟或重連；請分段錄製");
                elapsed = checked(elapsed + delta);
            }
            previous = first;
            yield return new(elapsed / 1e6, null, frame);
        }
    }

    static bool EncoderHeader(Stream stream)
    {
        using var header = EncoderMotion.ReadRecord(stream);
        if (header == null || !EncoderMotion.Text(header.RootElement, "type", "header") || !EncoderMotion.Text(header.RootElement, "format", "sick_encoder_frames") ||
            !EncoderMotion.Integer(header.RootElement, "version", uint.MaxValue, out uint version) || version != 1) throw new InvalidDataException("Unsupported or foreign encoder sidecar");
        return EncoderMotion.True(header.RootElement, "simulation");
    }

    static IEnumerable<MotionFrame> EncoderFrames(string source, int channel, long? declaredFrames)
    {
        string sidecar = Path.ChangeExtension(source, ".encoder.jsonl");
        if (!File.Exists(sidecar)) throw new InvalidDataException("Encoder mode requires the matching .encoder.jsonl recording; no time fallback");
        if (!declaredFrames.HasValue) throw new InvalidDataException("Raw PLY is missing its recorded frame count");
        using var raw = RawFrames(source).GetEnumerator();
        List<Vertex>? pending = raw.MoveNext() ? raw.Current : null;
        using var stream = File.OpenRead(sidecar); EncoderHeader(stream);
        uint frameCount = 0, previousCounter = 0; uint? previousTimestamp = null; uint[] previousPositions = [];
        double? previousRecordedElapsed = null; long position = 0; ulong elapsed = 0;
        while (true)
        {
            using var document = EncoderMotion.ReadRecord(stream);
            if (document == null) throw new InvalidDataException("Encoder recording is incomplete: missing completion footer");
            var record = document.RootElement;
            if (EncoderMotion.Text(record, "type", "end"))
            {
                if (!EncoderMotion.Integer(record, "frames", uint.MaxValue, out uint declared) || declared != frameCount || frameCount != declaredFrames || !EncoderMotion.True(record, "complete"))
                    throw new InvalidDataException("Encoder recording is incomplete or its frame count is incorrect");
                using var trailing = EncoderMotion.ReadRecord(stream);
                if (trailing != null) throw new InvalidDataException("Unexpected data after encoder completion footer");
                if (pending != null) throw new InvalidDataException("Raw PLY frame is missing from the encoder recording");
                if (frameCount == 0) throw new InvalidDataException("Encoder recording contains no scans");
                yield break;
            }
            if (!EncoderMotion.Text(record, "type", "frame") || !EncoderMotion.Integer(record, "frame_id", uint.MaxValue, out uint frame) || frame != frameCount ||
                !EncoderMotion.Integer(record, "timestamp_us", uint.MaxValue, out uint stamp) || !EncoderMotion.Integer(record, "scan_counter", ushort.MaxValue, out uint counter) ||
                !EncoderMotion.Integer(record, "device_status", ushort.MaxValue, out uint status) || !record.TryGetProperty("positions", out var positionsValue) || positionsValue.ValueKind != JsonValueKind.Array ||
                positionsValue.GetArrayLength() > 16 || !record.TryGetProperty("elapsed_s", out var time) || time.ValueKind != JsonValueKind.Number ||
                !time.TryGetDouble(out double recordedElapsed) || !double.IsFinite(recordedElapsed) || recordedElapsed < 0 || previousRecordedElapsed.HasValue && recordedElapsed < previousRecordedElapsed)
                throw new InvalidDataException("Encoder recording has invalid frame metadata or frame order");
            var positions = new uint[positionsValue.GetArrayLength()];
            for (int i = 0; i < positions.Length; i++) if (!EncoderMotion.Integer(positionsValue[i], uint.MaxValue, out positions[i])) throw new InvalidDataException("Encoder recording has invalid positions");
            frameCount = checked(frameCount + 1); previousRecordedElapsed = recordedElapsed;
            if (status != 0) throw new InvalidDataException("Encoder recording contains a scanner device fault");
            if (positions.Length < channel) throw new InvalidDataException($"Encoder recording is missing selected encoder channel {channel}");
            bool duplicate = false;
            if (previousTimestamp.HasValue)
            {
                if (stamp == previousTimestamp && counter == previousCounter)
                {
                    if (!positions.SequenceEqual(previousPositions)) throw new InvalidDataException("Duplicate encoder scan has conflicting positions");
                    duplicate = true;
                }
                else
                {
                    uint dt = unchecked(stamp - previousTimestamp.Value), dc = (counter - previousCounter) & 65535;
                    if (dt == 0 || dt >= 0x80000000 || dc == 0 || dc >= 0x8000) throw new InvalidDataException("Encoder timestamp/counter reset or discontinuity; record separate captures");
                    uint delta = unchecked(positions[channel - 1] - previousPositions[channel - 1]);
                    if (delta == 0x80000000) throw new InvalidDataException("Encoder half-range position jump is ambiguous");
                    position = checked(position + (delta < 0x80000000 ? delta : (long)delta - 0x100000000)); elapsed = checked(elapsed + dt);
                }
            }
            else position = positions[channel - 1];
            previousTimestamp = stamp; previousCounter = counter; previousPositions = positions;
            List<Vertex> points = [];
            if (pending != null)
            {
                if (pending[0].Frame < frame) throw new InvalidDataException("Raw PLY frame is missing from the encoder recording");
                if (pending[0].Frame == frame)
                {
                    if (pending[0].Timestamp != stamp || pending[0].Counter != counter) throw new InvalidDataException("Encoder timestamp/counter does not match raw PLY frame");
                    if (pending.Any(p => Math.Abs(p.Elapsed - recordedElapsed) > Math.Max(1e-12, 1e-12 * Math.Max(Math.Abs(p.Elapsed), Math.Abs(recordedElapsed))))) throw new InvalidDataException("Encoder elapsed time does not match raw PLY frame");
                    points = pending; pending = raw.MoveNext() ? raw.Current : null;
                }
            }
            if (!duplicate) yield return new(elapsed / 1e6, position, points);
        }
    }

    static IEnumerable<MotionFrame> Average(IEnumerable<MotionFrame> source, ReconstructionOptions settings, double end)
    {
        var mean = new PointMean(); var times = new List<double>(); var positions = new List<double>(); Vertex? first = null, previous = null;
        MotionFrame Result()
        {
            double elapsed = times.Average(); var vertices = new List<Vertex>(); var points = mean.Result();
            uint stamp = unchecked(first!.Timestamp + (uint)Math.Round((elapsed - times[0]) * 1e6));
            for (int ray = 0; ray < points.Length; ray++) if (points[ray] is P p)
                vertices.Add(new(p.X, p.Y, 0, first.Frame, stamp, first.Counter, (ushort)ray, first.Elapsed + elapsed - times[0]));
            return new(elapsed, positions.Count > 0 ? positions.Average() : null, vertices);
        }
        void Reset() { mean = new(); times.Clear(); positions.Clear(); first = null; }
        foreach (var frame in source)
        {
            if (frame.Time < settings.StartS || frame.Time > end) continue;
            if (frame.Points.Count == 0)
            {
                if (times.Count > 0) yield return Result();
                Reset(); previous = null; continue;
            }
            if (settings.AverageScans == 1) { yield return frame; continue; }
            var vertex = frame.Points[0];
            if (previous != null && (vertex.Frame != previous.Frame + 1 || ((vertex.Counter - previous.Counter) & 65535) != 1))
            { if (times.Count > 0) yield return Result(); Reset(); }
            previous = vertex; first ??= vertex;
            var rays = new P?[frame.Points.Max(v => (int)v.Ray) + 1]; foreach (var p in frame.Points) rays[p.Ray] = p.XY;
            mean.Add(rays); times.Add(frame.Time); if (frame.Position.HasValue) positions.Add(frame.Position.Value);
            if (times.Count == settings.AverageScans) { yield return Result(); Reset(); }
        }
        if (times.Count > 0) yield return Result();
    }

    public static Cloud OpenRaw(string path)
    {
        path = Path.GetFullPath(path); using var input = File.OpenRead(path); var header = Header(input);
        var preview = new List<Vertex>();
        foreach (var frame in RawFrames(path))
        {
            preview.AddRange(frame);
            break;
        }
        return new(preview, true, SourcePath: path) { PreviewOnly = true, DeclaredFrames = header.Frames, HeaderComments = header.Comments };
    }

    internal static void Validate(Calibration calibration, ReconstructionOptions s)
    {
        calibration.Validate(); double scale = 2 * Math.PI * s.WheelRadiusMm / s.EncoderCountsPerRev;
        if (new[] { s.TravelMm, s.StartS, s.MinHeight, s.MaxHeight, s.Xmin, s.Xmax, s.WheelRadiusMm, scale }.Any(v => !double.IsFinite(v)) ||
            s.TravelMm <= 0 || s.AverageScans < 1 || s.AverageScans > 600 || s.StartS < 0 || s.Direction != 1 && s.Direction != -1 || s.MinHeight >= s.MaxHeight || s.Xmin >= s.Xmax ||
            s.PositionSource is not ("time" or "encoder") || s.WheelRadiusMm <= 0 || s.EncoderCountsPerRev <= 0 || scale <= 0 || s.EncoderChannel < 1 || s.EncoderChannel > 16 ||
            s.EndS.HasValue && (!double.IsFinite(s.EndS.Value) || s.EndS.Value <= s.StartS) ||
            s.RoiXY != null && (s.RoiXY.Length != 4 || s.RoiXY.Any(v => !double.IsFinite(v)) || s.RoiXY[0] >= s.RoiXY[1] || s.RoiXY[2] >= s.RoiXY[3])) throw new ArgumentException("重建設定無效");
    }

    sealed class Bounds
    {
        readonly double[] minimum = [double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity], maximum = [double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity];
        long count;
        public void Add(Vertex point)
        {
            double[] values = [point.X, point.Y, point.Z];
            for (int i = 0; i < 3; i++) { minimum[i] = Math.Min(minimum[i], values[i]); maximum[i] = Math.Max(maximum[i], values[i]); }
            count++;
        }
        public Dictionary<string, object?> Report(string method)
        {
            if (count == 0) throw new ArgumentException("沒有可計算尺寸的點");
            bool estimated = method != "encoder_roll_travel"; string note = estimated ? "行程與時間估算" : "Encoder 計數與滾輪半徑換算";
            return new() { ["units"] = "mm", ["method"] = "visible_surface_axis_aligned_bounds", ["points"] = count,
                ["length_mm"] = maximum[1] - minimum[1], ["width_mm"] = maximum[0] - minimum[0], ["height_mm"] = maximum[2] - minimum[2],
                ["top_above_baseline_mm"] = maximum[2], ["min_xyz_mm"] = minimum, ["max_xyz_mm"] = maximum,
                ["length_estimated"] = estimated, ["position_method"] = method,
                ["note"] = $"可見點雲範圍：長=Y跨度（{note}）、寬=X跨度、高=Z跨度。裁切、缺面及離群點會影響結果；不是角鋼腿長或厚度。" };
        }
    }

    public static Cloud BuildAndSave(string source, string destination, Calibration calibration, ReconstructionOptions settings)
    {
        Validate(calibration, settings); source = Path.GetFullPath(source); destination = Path.GetFullPath(destination);
        string metadata = Path.ChangeExtension(destination, ".json");
        if (string.Equals(source, destination, StringComparison.OrdinalIgnoreCase) || File.Exists(destination) || File.Exists(metadata)) throw new IOException("輸出檔已存在或與原始檔相同；請選新檔名");
        RawHeader header; using (var input = File.OpenRead(source)) header = Header(input);
        JsonElement? capture = null; string capturePath = Path.ChangeExtension(source, ".capture.json");
        if (File.Exists(capturePath))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(capturePath)); capture = document.RootElement.Clone();
            if (capture.Value.ValueKind != JsonValueKind.Object) throw new InvalidDataException("錄製設定檔必須是 JSON 物件");
            if (capture.Value.TryGetProperty("error", out var error) && error.ValueKind != JsonValueKind.Null && error.ValueKind != JsonValueKind.False &&
                !(error.ValueKind == JsonValueKind.String && string.IsNullOrEmpty(error.GetString()))) throw new InvalidDataException("錄製有異常，請重新錄製：" + error);
        }
        bool encoder = settings.PositionSource == "encoder", simulation = false;
        if (encoder)
        {
            string sidecar = Path.ChangeExtension(source, ".encoder.jsonl");
            if (!File.Exists(sidecar)) throw new InvalidDataException("Encoder mode requires the matching .encoder.jsonl recording; no time fallback");
            using var input = File.OpenRead(sidecar); simulation = EncoderHeader(input) || capture is JsonElement data && EncoderMotion.True(data, "synthetic");
        }
        IEnumerable<MotionFrame> Frames() => encoder ? EncoderFrames(source, settings.EncoderChannel, header.Frames) : TimedFrames(source);
        long rawFrames = 0, total = 0; double duration = 0; double? origin = null, countMin = null, countMax = null;
        foreach (var frame in Frames())
        {
            duration = frame.Time; if (frame.Points.Count > 0) rawFrames++; total += frame.Points.Count;
            if (encoder && frame.Time >= settings.StartS && (!settings.EndS.HasValue || frame.Time <= settings.EndS.Value))
            {
                origin ??= frame.Position; countMin = Math.Min(countMin ?? frame.Position!.Value, frame.Position!.Value); countMax = Math.Max(countMax ?? frame.Position.Value, frame.Position.Value);
            }
        }
        double stop = settings.EndS ?? duration;
        if (rawFrames < 2 || stop <= settings.StartS || stop > duration + 1e-9) throw new ArgumentException("時間範圍無效或不足兩個有效截面");
        if (encoder && (!origin.HasValue || countMin == countMax)) throw new ArgumentException("Encoder 在選取範圍沒有位置變化；請確認接線、資料輸出及測輪轉動");
        double scale = 2 * Math.PI * settings.WheelRadiusMm / settings.EncoderCountsPerRev;
        string method = encoder ? "encoder_roll_travel" : "constant_speed_estimate_from_user_travel";
        string[] comments = encoder ? ["Y_encoder_roll_travel", $"encoder_wheel_radius_mm {settings.WheelRadiusMm.ToString("G17", CultureInfo.InvariantCulture)}",
            $"encoder_counts_per_rev {settings.EncoderCountsPerRev}", $"encoder_channel {settings.EncoderChannel}", $"encoder_mm_per_count {scale.ToString("G17", CultureInfo.InvariantCulture)}"] : ["Y_estimated_from_time_and_user_travel"];
        comments = [..comments, "averaged_metadata_first_source_frame_mean_timestamp"];
        string temporary = Path.Combine(Path.GetDirectoryName(destination)!, "manual_" + Guid.NewGuid().ToString("N") + ".tmp");
        bool plyCreated = false, jsonCreated = false; long written = 0, selectedFrames = 0, stride = Math.Max(1, (total + 11999) / 12000);
        var preview = new List<Vertex>(); var bounds = new Bounds();
        try
        {
            using (var output = new PlyWriter(temporary, false, settings.AverageScans, method, simulation, comments, trackFrames: false))
            {
                foreach (var frame in Average(Frames(), settings, stop))
                {
                    double y = encoder ? settings.Direction * (frame.Position!.Value - origin!.Value) * scale : settings.Direction * settings.TravelMm * (frame.Time - settings.StartS) / (stop - settings.StartS);
                    bool added = false;
                    foreach (var vertex in frame.Points)
                    {
                        var p = vertex.XY; var roi = settings.RoiXY;
                        if (roi != null && (p.X < roi[0] || p.X > roi[1] || p.Y < roi[2] || p.Y > roi[3])) continue;
                        var v = calibration.Transform(p);
                        if (!(settings.Xmin <= v.X && v.X <= settings.Xmax && settings.MinHeight <= v.Y && v.Y <= settings.MaxHeight)) continue;
                        var point = vertex with { X = v.X, Y = y, Z = v.Y }; output.Add(point); bounds.Add(point);
                        if (written % stride == 0) preview.Add(point);
                        written++; added = true;
                    }
                    if (added) selectedFrames++;
                }
                if (selectedFrames < 2) throw new ArgumentException("篩選／平均後不足兩個有效截面，請減少平均圈數或檢查校正、範圍與裁切時間");
                output.Complete(selectedFrames);
            }
            var report = new Dictionary<string, object?>
            {
                ["source"] = source, ["output"] = destination, ["units"] = "mm", ["position_method"] = method,
                ["warning"] = encoder ? "Y uses encoder wheel travel; effective radius and wheel slip affect its scale." : "Y is estimated, not measured. Uneven manual motion distorts geometry.",
                ["axes"] = new { x = "along fixed baseline", y = encoder ? "encoder wheel travel" : "estimated travel", z = "height toward sensor" },
                ["calibration"] = new { first = new[] { calibration.First.X, calibration.First.Y }, second = new[] { calibration.Second.X, calibration.Second.Y } },
                ["settings"] = new { travel_mm = settings.TravelMm, start_s = settings.StartS, end_s = settings.EndS, xmin = settings.Xmin, xmax = settings.Xmax,
                    min_height = settings.MinHeight, max_height = settings.MaxHeight, direction = settings.Direction, average_scans = settings.AverageScans,
                    roi_xy = settings.RoiXY, position_source = settings.PositionSource, wheel_radius_mm = settings.WheelRadiusMm, encoder_counts_per_rev = settings.EncoderCountsPerRev, encoder_channel = settings.EncoderChannel },
                ["raw_duration_s"] = duration, ["effective_end_s"] = stop, ["raw_frames_with_points"] = rawFrames, ["output_frames"] = selectedFrames, ["output_points"] = written,
                ["dimensions"] = bounds.Report(method), ["capture"] = capture,
                ["averaging"] = new { scans_per_block = settings.AverageScans, method = "majority_range_cluster_mean_v2", minimum_support_fraction = .6,
                    position = encoder ? "mean_encoder_distance" : "mean_device_time", edge_threshold_mm = 10, metadata = "first_source_frame_and_counter; mean_timestamp" }
            };
            if (encoder)
            {
                report["simulation"] = simulation;
                report["position_reference"] = new { sidecar = Path.ChangeExtension(source, ".encoder.jsonl"), wheel_radius_mm = settings.WheelRadiusMm,
                    encoder_counts_per_rev = settings.EncoderCountsPerRev, encoder_channel = settings.EncoderChannel, mm_per_count = scale,
                    first_selected_unwrapped_count = origin, selected_count_range = new[] { countMin, countMax }, source = simulation ? "synthetic" : "recorded", simulation };
            }
            File.Move(temporary, destination, overwrite: false); plyCreated = true;
            using (var output = new FileStream(metadata, FileMode.CreateNew, FileAccess.Write)) { jsonCreated = true; JsonSerializer.Serialize(output, report, ScanJson.Options); }
            return new(preview, false, settings.AverageScans, method, simulation, destination)
            { PreviewOnly = true, DeclaredFrames = selectedFrames, HeaderComments = comments, ReconstructionReport = report };
        }
        catch
        {
            if (jsonCreated) File.Delete(metadata);
            if (plyCreated) File.Delete(destination);
            throw;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
