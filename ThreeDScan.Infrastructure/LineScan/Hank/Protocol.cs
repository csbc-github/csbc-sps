using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;
namespace ThreeDScan.Infrastructure.LineScan.Hank;
public sealed record EncoderReading(uint Position, ushort Reserved);
public record Scan(ushort Counter, uint Timestamp, double Frequency, int Status, P?[] Points)
{
    public IReadOnlyList<EncoderReading> Encoders { get; init; } = Array.Empty<EncoderReading>();
    public ushort TelegramCounter { get; init; }
    public double[] Angles { get; init; } = [];
    public double?[] Distances { get; init; } = [];
    public string[] Channels { get; init; } = [];
    public bool AngleCorrected { get; init; }
}
public static class Protocol
{
    public static byte[] Frame(byte[] p) { var b = new byte[p.Length + 9]; Array.Fill<byte>(b, 2, 0, 4); BinaryPrimitives.WriteInt32BigEndian(b.AsSpan(4), p.Length); p.CopyTo(b, 8); foreach (byte x in p) b[^1] ^= x; return b; }
    public static byte[] Subscription(bool on) => Frame(Encoding.ASCII.GetBytes("sEN LMDscandata ").Concat(new[] { on ? (byte)1 : (byte)0 }).ToArray());
    public sealed class Decoder
    {
        readonly List<byte> bytes = []; public int Errors { get; private set; }
        public List<byte[]> Feed(byte[] data, int count) { bytes.AddRange(data.Take(count)); var result = new List<byte[]>(); while (true) { int i = 0; while (i + 3 < bytes.Count && !(bytes[i] == 2 && bytes[i + 1] == 2 && bytes[i + 2] == 2 && bytes[i + 3] == 2)) i++; if (i > 0) bytes.RemoveRange(0, i); if (bytes.Count < 8) break; int n = (bytes[4] << 24) | (bytes[5] << 16) | (bytes[6] << 8) | bytes[7]; if (n < 1 || n > 1048576) { Errors++; bytes.RemoveRange(0, 4); continue; } if (bytes.Count < n + 9) break; var p = bytes.GetRange(8, n).ToArray(); byte sum = 0; foreach (var x in p) sum ^= x; if (sum == bytes[n + 8]) result.Add(p); else Errors++; bytes.RemoveRange(0, n + 9); } return result; }
    }
    sealed class Reader(byte[] bytes, int start = 0)
    {
        public int Position = start;
        void Require(int n) { if (n < 0 || Position > bytes.Length - n) throw new ArgumentException("量測封包資料不完整"); }
        public byte U8() { Require(1); return bytes[Position++]; }
        public ushort U16() { Require(2); var n = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(Position, 2)); Position += 2; return n; }
        public uint U32() { Require(4); var n = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(Position, 4)); Position += 4; return n; }
        public float F32() => BitConverter.Int32BitsToSingle(unchecked((int)U32()));
        public string Text(int n) { Require(n); if (bytes.AsSpan(Position, n).ContainsAnyInRange((byte)128, byte.MaxValue)) throw new ArgumentException("通道名稱必須是 ASCII"); var s = Encoding.ASCII.GetString(bytes, Position, n); Position += n; return s; }
    }
    record Channel(float Scale, float Bias, int Start, int Step, int[] Values);
    public static Scan ParseEncoderScan(byte[] p)
    {
        if (p.Length < 54 || !(Encoding.ASCII.GetString(p, 0, 16) is "sSN LMDscandata " or "sRA LMDscandata ")) throw new ArgumentException("非掃描封包");
        if (BinaryPrimitives.ReadUInt16BigEndian(p.AsSpan(16, 2)) != 1) throw new ArgumentException("版本不符");
        int count = BinaryPrimitives.ReadUInt16BigEndian(p.AsSpan(52, 2));
        if (count > 16 || p.Length < 54 + count * 6) throw new ArgumentException("Encoder 資料不完整或過多");
        var encoders = Enumerable.Range(0, count).Select(i => new EncoderReading(BinaryPrimitives.ReadUInt32BigEndian(p.AsSpan(54 + i * 6, 4)), BinaryPrimitives.ReadUInt16BigEndian(p.AsSpan(58 + i * 6, 2)))).ToArray();
        return new(BinaryPrimitives.ReadUInt16BigEndian(p.AsSpan(28, 2)), BinaryPrimitives.ReadUInt32BigEndian(p.AsSpan(30, 4)), BinaryPrimitives.ReadUInt32BigEndian(p.AsSpan(44, 4)) / 100.0, p[24] | p[25] << 8, [])
        { Encoders = encoders, TelegramCounter = BinaryPrimitives.ReadUInt16BigEndian(p.AsSpan(26, 2)) };
    }
    public static Scan Parse(byte[] p)
    {
        if (p.Length < 54 || !(Encoding.ASCII.GetString(p, 0, 16) is "sSN LMDscandata " or "sRA LMDscandata ")) throw new ArgumentException("非掃描封包"); var r = new Reader(p, 16); if (r.U16() != 1) throw new ArgumentException("版本不符"); r.U16(); r.U32(); int status = r.U8() | (r.U8() << 8); ushort telegram = r.U16(); ushort counter = r.U16(); uint stamp = r.U32(); r.U32(); r.U16(); r.U16(); r.U16(); double frequency = r.U32() / 100.0; r.U32(); int encoders = r.U16(); if (encoders > 16) throw new ArgumentException("encoder 過多");
        var readings = new EncoderReading[encoders];
        for (int i = 0; i < encoders; i++) readings[i] = new(r.U32(), r.U16());
        var channels = new Dictionary<string, Channel>();
        foreach (int width in new[] { 2, 1 }) { int count = r.U16(); if (count > 32) throw new ArgumentException("通道過多"); for (int k = 0; k < count; k++) { var name = r.Text(5); float scale = r.F32(), bias = r.F32(); int start = unchecked((int)r.U32()), step = r.U16(), n = r.U16(); if (n > 16384 || !float.IsFinite(scale) || !float.IsFinite(bias)) throw new ArgumentException("通道無效"); var values = new int[n]; for (int j = 0; j < n; j++) values[j] = width == 2 ? r.U16() : r.U8(); if (!channels.TryAdd(name, new(scale, bias, start, step, values))) throw new ArgumentException("重複通道"); } }
        if (!channels.TryGetValue("DIST1", out var d) || d.Scale <= 0 || d.Values.Length > 1 && d.Step == 0) throw new ArgumentException("DIST1 無效"); channels.TryGetValue("ANGL1", out var a); channels.TryGetValue("QLTY1", out var q); foreach (var ch in new[] { a, q }) if (ch != null && (ch.Values.Length != d.Values.Length || ch.Start != d.Start || ch.Step != d.Step)) throw new ArgumentException("通道不一致"); var points = new P?[d.Values.Length]; var angles = new double[d.Values.Length]; var distances = new double?[d.Values.Length]; for (int i = 0; i < points.Length; i++) { double degrees = (d.Start + i * (double)d.Step) / 10000.0; if (a != null) degrees += (a.Values[i] * (double)a.Scale + a.Bias) / 10000.0; angles[i] = degrees; double angle = degrees * (Math.PI / 180); double radius = d.Values[i] * (double)d.Scale + d.Bias; if (d.Values[i] >= 16 && radius > 0 && (q == null || (q.Values[i] & 15) == 0)) { points[i] = new(radius * Math.Cos(angle), radius * Math.Sin(angle)); distances[i] = radius; } }
        return new(counter, stamp, frequency, status, points) { Encoders = Array.AsReadOnly(readings), TelegramCounter = telegram, Angles = angles, Distances = distances, Channels = channels.Keys.ToArray(), AngleCorrected = a != null };
    }
}
public sealed class PointMean
{
    readonly Dictionary<int, List<(double Radius, int Frame, P Point)>> samples = []; public int Frames { get; private set; }
    public void Add(P?[] points) { Frames++; for (int i = 0; i < points.Length; i++) if (points[i] is P p) { if (!samples.TryGetValue(i, out var v)) samples[i] = v = []; v.Add((p.Length, Frames, p)); } }
    public P?[] Result() { if (samples.Count == 0) return []; var output = new P?[samples.Keys.Max() + 1]; int minimum = (int)Math.Ceiling(Frames * .6), recent = Math.Min(Frames, Math.Max(3, (int)Math.Ceiling(Frames * .25))); foreach (var (i, values) in samples) { var v = values.OrderBy(p => p.Radius).ToArray(); int left = 0, bs = 0, be = 0; for (int right = 0; right < v.Length; right++) { while (v[right].Radius - v[left].Radius > 10) left++; if (right + 1 - left > be - bs) { bs = left; be = right + 1; } } var cluster = v[bs..be]; if (cluster.Length < minimum || cluster.Count(p => p.Frame > Frames - recent) < Math.Ceiling(recent * .6)) continue; output[i] = new(cluster.Average(p => p.Point.X), cluster.Average(p => p.Point.Y)); } return output; }
}
public sealed class Scanner : IAsyncDisposable
{
    readonly bool encoderOnly;
    public Scanner(bool encoderOnly = false) { this.encoderOnly = encoderOnly; }
    readonly CancellationTokenSource cancel = new();
    Task? worker;
    int disposed;
    public event Action<Scan>? Received;
    public event Action<string>? State;
    public long Count, Gaps, Bytes, BadFrames, Duplicates, Reconnections;
    public bool Connected { get; private set; }
    public string Host { get; private set; } = "192.168.0.2";
    public int Port { get; private set; } = 2111;
    public void Start(string ip, int port)
    {
        if (worker != null) throw new InvalidOperationException("接收已啟動");
        if (port < 1 || port > 65535) throw new ArgumentException("Port 必須為 1–65535");
        Host = ip; Port = port;
        worker = Task.Run(() => Run(ip, port));
    }
    async Task Run(string ip, int port)
    {
        bool connectedBefore = false;
        while (!cancel.IsCancellationRequested)
        {
            using var client = new TcpClient();
            try
            {
                await client.ConnectAsync(ip, port, cancel.Token); client.NoDelay = true;
                var stream = client.GetStream();
                await stream.WriteAsync(Protocol.Subscription(true), cancel.Token);
                if (connectedBefore) Interlocked.Increment(ref Reconnections);
                connectedBefore = true; Connected = true; State?.Invoke("已連線");
                var decoder = new Protocol.Decoder(); var bytes = new byte[65536]; ushort? previous = null;
                try
                {
                    while (!cancel.IsCancellationRequested)
                    {
                        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel.Token); timeout.CancelAfter(10000);
                        int n = await stream.ReadAsync(bytes, timeout.Token);
                        if (n == 0) throw new IOException("連線中斷");
                        Interlocked.Add(ref Bytes, n);
                        int before = decoder.Errors;
                        var payloads = decoder.Feed(bytes, n);
                        Interlocked.Add(ref BadFrames, decoder.Errors - before);
                        foreach (var payload in payloads)
                        {
                            if (payload.Length < 16 || !(Encoding.ASCII.GetString(payload, 0, 16) is "sSN LMDscandata " or "sRA LMDscandata ")) continue;
                            Scan scan;
                            try { scan = encoderOnly ? Protocol.ParseEncoderScan(payload) : Protocol.Parse(payload); }
                            catch (Exception e) when (e is ArgumentException or IndexOutOfRangeException)
                            { Interlocked.Increment(ref BadFrames); State?.Invoke("封包錯誤：" + e.Message); continue; }
                            Interlocked.Increment(ref Count);
                            if (previous is ushort old)
                            {
                                int delta = (scan.Counter - old) & 65535;
                                if (delta == 0) Interlocked.Increment(ref Duplicates);
                                else Interlocked.Add(ref Gaps, Math.Max(0, delta - 1));
                            }
                            previous = scan.Counter;
                            Received?.Invoke(scan);
                        }
                    }
                }
                finally
                {
                    Connected = false;
                    try { using var unsubscribe = new CancellationTokenSource(1000); await stream.WriteAsync(Protocol.Subscription(false), unsubscribe.Token); }
                    catch (Exception e) when (e is IOException or SocketException or OperationCanceledException or ObjectDisposedException) { }
                }
            }
            catch (Exception e) when (e is IOException or SocketException or OperationCanceledException)
            { Connected = false; if (!cancel.IsCancellationRequested) State?.Invoke(e.Message); }
            if (!cancel.IsCancellationRequested)
                try { await Task.Delay(1000, cancel.Token); } catch (OperationCanceledException) { }
        }
    }
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        cancel.Cancel();
        if (worker != null) await worker;
        cancel.Dispose();
    }
}
