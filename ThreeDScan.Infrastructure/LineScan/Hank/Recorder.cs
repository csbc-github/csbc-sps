using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace ThreeDScan.Infrastructure.LineScan.Hank;

public sealed class Recorder
{
    readonly Channel<(Scan Scan, double Time)> queue;
    readonly Task task;
    readonly Stopwatch clock = Stopwatch.StartNew();
    readonly object gate = new();
    readonly string path, encoderPath, capturePath;
    readonly double[]? roi;
    readonly object? context;
    readonly DateTimeOffset started = DateTimeOffset.UtcNow;
    string? error;
    bool stopping, finished;
    public string? Error { get { lock (gate) return error; } }

    public Recorder(string path, double[]? roi = null, object? context = null, int queueCapacity = 128)
    {
        this.path = Path.GetFullPath(path);
        encoderPath = Path.ChangeExtension(this.path, ".encoder.jsonl");
        capturePath = Path.ChangeExtension(this.path, ".capture.json");
        if (roi != null && (roi.Length != 4 || roi.Any(v => !double.IsFinite(v)) || roi[0] >= roi[1] || roi[2] >= roi[3])) throw new ArgumentException("錄製框選範圍無效");
        this.roi = roi?.ToArray(); this.context = context;
        queue = Channel.CreateBounded<(Scan, double)>(queueCapacity);
        FileStream? encoder = null, capture = null; PlyWriter? writer = null;
        try
        {
            encoder = new(encoderPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read);
            capture = new(capturePath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read);
            WriteJson(encoder, new { type = "header", format = "sick_encoder_frames", version = 1, simulation = false, source = "recorded" }); encoder.Flush();
            writer = new PlyWriter(this.path, true);
            var ownedEncoder = encoder; var ownedCapture = capture; var ownedWriter = writer;
            task = Task.Run(() => Run(ownedWriter, ownedEncoder, ownedCapture));
        }
        catch
        {
            writer?.Dispose(); encoder?.Dispose(); capture?.Dispose();
            if (encoder != null) File.Delete(encoderPath);
            if (capture != null) File.Delete(capturePath);
            throw;
        }
    }
    public void Submit(Scan scan)
    {
        lock (gate)
        {
            if (stopping) return;
            if (!queue.Writer.TryWrite((scan, clock.Elapsed.TotalSeconds))) { error = "錄製佇列已滿或寫入中斷，檔案標示未完成"; stopping = true; queue.Writer.TryComplete(); }
        }
    }
    public void Fail(string reason)
    {
        lock (gate) { if (finished) return; error ??= reason; stopping = true; queue.Writer.TryComplete(); }
    }
    public async Task Stop() { lock (gate) { stopping = true; queue.Writer.TryComplete(); } await task.ConfigureAwait(false); }
    static void WriteJson(Stream stream, object value) { stream.Write(JsonSerializer.SerializeToUtf8Bytes(value)); stream.WriteByte(10); }

    async Task Run(PlyWriter writer, FileStream encoder, FileStream capture)
    {
        uint frames = 0; long points = 0, encoderOffset = encoder.Position; Scan? previous = null;
        try
        {
            await foreach (var (scan, time) in queue.Reader.ReadAllAsync())
            {
                if (scan.Status != 0) throw new InvalidDataException("掃描器回報設備異常，請分段重新錄製");
                if (scan.Points.Length > 65536 || scan.Encoders.Count > 16 || scan.Points.Any(p => p is P v && (!double.IsFinite(v.X) || !double.IsFinite(v.Y)))) throw new InvalidDataException("錄製資料包含無效點或 Encoder 通道");
                if (previous != null)
                {
                    uint dt = unchecked(scan.Timestamp - previous.Timestamp); int dc = (scan.Counter - previous.Counter) & 65535;
                    if (dt == 0 && dc == 0) { if (!scan.Encoders.Select(e => e.Position).SequenceEqual(previous.Encoders.Select(e => e.Position))) throw new InvalidDataException("重複掃描的 Encoder 位置衝突"); }
                    else if (dt == 0 || dt >= 0x80000000 || dc == 0 || dc >= 0x8000) throw new InvalidDataException("掃描時間或計數器中斷，請分段重新錄製");
                }
                long added = 0;
                for (int i = 0; i < scan.Points.Length; i++)
                    if (scan.Points[i] is P p && (roi == null || p.X >= roi[0] && p.X <= roi[1] && p.Y >= roi[2] && p.Y <= roi[3])) { writer.Add(new(p.X, p.Y, 0, frames, scan.Timestamp, scan.Counter, (ushort)i, time)); added++; }
                WriteJson(encoder, new { type = "frame", frame_id = frames, timestamp_us = scan.Timestamp, scan_counter = scan.Counter, elapsed_s = time, device_status = scan.Status, positions = scan.Encoders.Select(e => e.Position).ToArray() });
                encoderOffset = encoder.Position; points += added; frames++; previous = scan;
            }
        }
        catch (Exception e) { Fail("點雲錄製失敗：" + e.Message); }
        finally
        {
            lock (gate)
            {
            try
            {
                encoder.Position = encoderOffset; WriteJson(encoder, new { type = "end", frames, complete = Error == null }); encoder.SetLength(encoder.Position); encoder.Flush(true); encoder.Dispose();
                WriteCapture(capture, frames, points); capture.Flush(true); capture.Dispose();
                if (Error == null) writer.Complete(frames);
            }
            catch (Exception e) { Fail("錄製完成標記寫入失敗：" + e.Message); }
            try { writer.Dispose(); } catch (Exception e) { Fail("PLY 關閉失敗：" + e.Message); }
            try { encoder.Dispose(); } catch (Exception e) { Fail(e.Message); }
            try { capture.Dispose(); } catch (Exception e) { Fail(e.Message); }
            if (Error != null)
            {
                // Repair only owned files when a final flush failed after buffering a completion marker.
                try { RepairPly(frames, points); } catch (IOException) { }
                try { using var repair = new FileStream(encoderPath, FileMode.Open, FileAccess.Write); repair.Position = encoderOffset; WriteJson(repair, new { type = "end", frames, complete = false }); repair.SetLength(repair.Position); repair.Flush(true); } catch (IOException) { }
                try { using var repair = new FileStream(capturePath, FileMode.Open, FileAccess.Write); WriteCapture(repair, frames, points); repair.SetLength(repair.Position); repair.Flush(true); } catch (IOException) { }
            }
            stopping = true; finished = true;
            }
        }
    }
    void WriteCapture(Stream output, uint frames, long points)
    {
        var metadata = new Dictionary<string, object?>();
        if (context != null)
        {
            var snapshot = JsonSerializer.SerializeToElement(context, ScanJson.Options);
            if (snapshot.ValueKind == JsonValueKind.Object) foreach (var p in snapshot.EnumerateObject()) metadata[p.Name] = p.Value.Clone();
        }
        metadata["source"] = path; metadata["started_utc"] = started; metadata["completed_utc"] = DateTimeOffset.UtcNow;
        metadata["synthetic"] = false; metadata["complete"] = Error == null; metadata["error"] = Error ?? "";
        metadata["encoder_path"] = encoderPath; metadata["encoder_frames"] = frames; metadata["frames"] = frames; metadata["points"] = points;
        if (!metadata.ContainsKey("roi_xy")) metadata["roi_xy"] = roi;
        output.Write(JsonSerializer.SerializeToUtf8Bytes(metadata, ScanJson.Options));
    }
    void RepairPly(uint frames, long points)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.ReadWrite); var headerBytes = new List<byte>();
        while (headerBytes.Count < 65536) { int b = file.ReadByte(); if (b < 0) return; headerBytes.Add((byte)b); if (b == 10 && Encoding.ASCII.GetString(headerBytes.ToArray()).EndsWith("end_header\n", StringComparison.Ordinal)) break; }
        var header = Encoding.ASCII.GetString(headerBytes.ToArray());
        void Patch(string key, string value) { int offset = header.IndexOf(key, StringComparison.Ordinal); if (offset < 0) return; file.Position = offset + key.Length; file.Write(Encoding.ASCII.GetBytes(value)); }
        Patch("comment recording_complete ", "0"); Patch("comment frames_written ", frames.ToString("D20", CultureInfo.InvariantCulture)); Patch("element vertex ", points.ToString("D20", CultureInfo.InvariantCulture));
        file.SetLength(headerBytes.Count + points * 32); file.Flush(true);
    }
}
