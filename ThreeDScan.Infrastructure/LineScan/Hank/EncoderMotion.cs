using System.Text.Json;
namespace ThreeDScan.Infrastructure.LineScan.Hank;

internal record MotionFrame(double Time, double? Position, List<Vertex> Points);

internal sealed record EncoderMotion(List<MotionFrame> Frames, bool Simulation)
{
    internal static JsonDocument? ReadRecord(Stream stream)
    {
        using var bytes = new MemoryStream();
        while (true)
        {
            int value = stream.ReadByte();
            if (value < 0) { if (bytes.Length == 0) return null; throw new InvalidDataException("Encoder sidecar line is incomplete or too large"); }
            bytes.WriteByte((byte)value);
            if (bytes.Length > 65536) throw new InvalidDataException("Encoder sidecar line is incomplete or too large");
            if (value == 10) break;
        }
        try
        {
            var document = JsonDocument.Parse(bytes.ToArray());
            try
            {
                if (document.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Encoder sidecar record must be an object");
                UniqueKeys(document.RootElement); return document;
            }
            catch { document.Dispose(); throw; }
        }
        catch (JsonException e) { throw new InvalidDataException("Encoder sidecar contains invalid JSON", e); }
    }
    static void UniqueKeys(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject()) { if (!names.Add(property.Name)) throw new InvalidDataException("Encoder sidecar contains duplicate JSON keys"); UniqueKeys(property.Value); }
        }
        else if (value.ValueKind == JsonValueKind.Array) foreach (var item in value.EnumerateArray()) UniqueKeys(item);
    }
    internal static bool Text(JsonElement value, string name, string expected) => value.TryGetProperty(name, out var item) && item.ValueKind == JsonValueKind.String && item.GetString() == expected;
    internal static bool Integer(JsonElement value, string name, uint maximum, out uint result) { result = 0; return value.TryGetProperty(name, out var item) && Integer(item, maximum, out result); }
    internal static bool Integer(JsonElement item, uint maximum, out uint result)
    {
        result = 0;
        return item.ValueKind == JsonValueKind.Number && item.GetRawText().All(c => c is >= '0' and <= '9') && item.TryGetUInt32(out result) && result <= maximum;
    }
    internal static bool True(JsonElement value, string name) => value.TryGetProperty(name, out var item) && item.ValueKind == JsonValueKind.True;

    public static EncoderMotion Read(Cloud source, List<List<Vertex>> raw, int channel)
    {
        if (source.SourcePath == null) throw new InvalidDataException("Encoder mode requires the matching .encoder.jsonl recording; no time fallback");
        string sidecar = Path.ChangeExtension(source.SourcePath, ".encoder.jsonl");
        if (!File.Exists(sidecar)) throw new InvalidDataException("Encoder mode requires the matching .encoder.jsonl recording; no time fallback");
        if (source.DeclaredFrames == null || source.DeclaredFrames < 0) throw new InvalidDataException("Raw PLY is missing its recorded frame count");
        using var stream = File.OpenRead(sidecar); using var header = ReadRecord(stream);
        if (header == null || !Text(header.RootElement, "type", "header") || !Text(header.RootElement, "format", "sick_encoder_frames") || !Integer(header.RootElement, "version", uint.MaxValue, out uint version) || version != 1) throw new InvalidDataException("Unsupported or foreign encoder sidecar");
        bool simulation = True(header.RootElement, "simulation");
        var frames = new List<MotionFrame>(); uint frameCount = 0; int rawIndex = 0; uint? previousTimestamp = null; uint previousCounter = 0;
        uint[] previousPositions = []; double? previousRecordedElapsed = null; ulong elapsed = 0; long position = 0;
        while (true)
        {
            using var document = ReadRecord(stream);
            if (document == null) throw new InvalidDataException("Encoder recording is incomplete: missing completion footer");
            var record = document.RootElement;
            if (Text(record, "type", "end"))
            {
                if (!Integer(record, "frames", uint.MaxValue, out uint declared) || declared != frameCount || frameCount != source.DeclaredFrames || !True(record, "complete")) throw new InvalidDataException("Encoder recording is incomplete or its frame count is incorrect");
                using var trailing = ReadRecord(stream);
                if (trailing != null) throw new InvalidDataException("Unexpected data after encoder completion footer");
                if (rawIndex != raw.Count) throw new InvalidDataException("Raw PLY frame is missing from the encoder recording");
                if (frameCount == 0) throw new InvalidDataException("Encoder recording contains no scans"); break;
            }
            if (!Text(record, "type", "frame") || !Integer(record, "frame_id", uint.MaxValue, out uint frame) || frame != frameCount ||
                !Integer(record, "timestamp_us", uint.MaxValue, out uint stamp) || !Integer(record, "scan_counter", ushort.MaxValue, out uint counter) ||
                !Integer(record, "device_status", ushort.MaxValue, out uint status) || !record.TryGetProperty("positions", out var positionsValue) || positionsValue.ValueKind != JsonValueKind.Array ||
                positionsValue.GetArrayLength() > 16 || !record.TryGetProperty("elapsed_s", out var time) || time.ValueKind != JsonValueKind.Number ||
                !time.TryGetDouble(out double recordedElapsed) || !double.IsFinite(recordedElapsed) || recordedElapsed < 0 || previousRecordedElapsed.HasValue && recordedElapsed < previousRecordedElapsed)
                throw new InvalidDataException("Encoder recording has invalid frame metadata or frame order");
            var positions = new uint[positionsValue.GetArrayLength()];
            for (int i = 0; i < positions.Length; i++) if (!Integer(positionsValue[i], uint.MaxValue, out positions[i])) throw new InvalidDataException("Encoder recording has invalid positions");
            frameCount = checked(frameCount + 1); previousRecordedElapsed = recordedElapsed;
            if (status != 0) throw new InvalidDataException("Encoder recording contains a scanner device fault");
            if (positions.Length < channel) throw new InvalidDataException($"Encoder recording is missing selected encoder channel {channel}");
            bool duplicate = false;
            if (previousTimestamp.HasValue)
            {
                if (stamp == previousTimestamp && counter == previousCounter)
                {
                    if (!positions.SequenceEqual(previousPositions)) throw new InvalidDataException("Duplicate encoder scan has conflicting positions"); duplicate = true;
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
            if (rawIndex < raw.Count)
            {
                var pending = raw[rawIndex];
                if (pending[0].Frame < frame) throw new InvalidDataException("Raw PLY frame is missing from the encoder recording");
                if (pending[0].Frame == frame)
                {
                    if (pending[0].Timestamp != stamp || pending[0].Counter != counter) throw new InvalidDataException("Encoder timestamp/counter does not match raw PLY frame");
                    if (pending.Any(p => Math.Abs(p.Elapsed - recordedElapsed) > Math.Max(1e-12, 1e-12 * Math.Max(Math.Abs(p.Elapsed), Math.Abs(recordedElapsed))))) throw new InvalidDataException("Encoder elapsed time does not match raw PLY frame");
                    points = pending; rawIndex++;
                }
            }
            if (!duplicate) frames.Add(new(elapsed / 1e6, position, points));
        }
        return new(frames, simulation);
    }
}
