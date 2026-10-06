using System.IO;
using System.Net.Sockets;
using System.Text;
using LMX;

namespace ThreeDScan.LineScan;

/// <summary>連線掃描器 CoLa B，把每一圈截面轉成進料區剖面圖用的影格。</summary>
internal sealed class SickLiveScanner : IDisposable
{
    private const int MaxPayload = 1024 * 1024;

    private static readonly byte[] Magic = [0x02, 0x02, 0x02, 0x02];
    private static readonly byte[] StreamOn = Encoding.ASCII.GetBytes("sEN LMDscandata \u0001");
    private static readonly byte[] StreamOff = Encoding.ASCII.GetBytes("sEN LMDscandata \u0000");

    private CancellationTokenSource? _cts;
    private TcpClient? _client;
    private int _frameNumber;
    private long _lastRaisedTicks;

    /// <summary>收到可畫的截面。</summary>
    public event EventHandler<LidarFrame>? FrameReceived;

    /// <summary>連線狀態說明，供左側空畫面顯示。</summary>
    public event EventHandler<string>? StatusChanged;

    /// <summary>是否正在嘗試接收。</summary>
    public bool IsRunning
    {
        get
        {
            return _cts != null;
        }
    }

    /// <summary>開始連到指定光達。已在跑時會先停掉再重連。</summary>
    public void Start(string ip, int port)
    {
        Stop();
        var cts = new CancellationTokenSource();
        _cts = cts;
        Task.Run(() => Run(ip, port, cts));
    }

    /// <summary>停止接收並關閉連線，把光達讓給錄製。</summary>
    public void Stop()
    {
        var cts = Interlocked.Exchange(ref _cts, null);
        if (cts != null)
        {
            try
            {
                cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        CloseClient();
    }

    /// <summary>關掉目前的 TCP，讓阻塞中的讀取立刻結束。</summary>
    private void CloseClient()
    {
        var client = Interlocked.Exchange(ref _client, null);
        if (client == null)
        {
            return;
        }

        try
        {
            client.Close();
        }
        catch (ObjectDisposedException)
        {
        }
        catch (SocketException)
        {
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Stop();
    }

    private void Run(string ip, int port, CancellationTokenSource cts)
    {
        var token = cts.Token;
        try
        {
            RunLoop(ip, port, token);
        }
        catch (ObjectDisposedException)
        {
        }
        finally
        {
            cts.Dispose();
        }
    }

    private void RunLoop(string ip, int port, CancellationToken token)
    {
        var attempts = 0;
        while (!token.IsCancellationRequested)
        {
            TcpClient? client = null;
            NetworkStream? stream = null;
            var subscribed = false;
            RaiseStatus("正在連接 " + ip + ":" + port);
            try
            {
                client = new TcpClient();
                _client = client;
                var connect = client.ConnectAsync(ip, port, token).AsTask();
                if (!connect.Wait(2000, token))
                {
                    throw new TimeoutException("連線逾時");
                }

                stream = client.GetStream();
                stream.ReadTimeout = 250;
                WriteFrame(stream, StreamOn);
                subscribed = true;
                RaiseStatus("已連線，等待截面");
                if (attempts > 0)
                {
                    RaiseStatus("已重新連上 " + ip + ":" + port);
                }

                var buffer = new byte[65536];
                var pending = new List<byte>(65536);
                var lastData = Environment.TickCount64;
                while (!token.IsCancellationRequested)
                {
                    int read;
                    try
                    {
                        read = stream.Read(buffer, 0, buffer.Length);
                    }
                    catch (IOException) when (Environment.TickCount64 - lastData < 10000)
                    {
                        if (Environment.TickCount64 - lastData > 3000)
                        {
                            RaiseStatus("已連線，尚未收到截面");
                        }

                        continue;
                    }

                    if (read <= 0)
                    {
                        throw new IOException("對方已關閉連線");
                    }

                    lastData = Environment.TickCount64;
                    for (var i = 0; i < read; i++)
                    {
                        pending.Add(buffer[i]);
                    }

                    foreach (var payload in TakePayloads(pending))
                    {
                        if (TryCreateFrame(payload, out var frame) && frame != null)
                        {
                            var now = Environment.TickCount64;
                            if (now - _lastRaisedTicks >= 66)
                            {
                                _lastRaisedTicks = now;
                                FrameReceived?.Invoke(this, frame);
                            }
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is SocketException or IOException or TimeoutException or InvalidOperationException or OperationCanceledException or AggregateException or ObjectDisposedException)
            {
                if (!token.IsCancellationRequested)
                {
                    RaiseStatus("線掃描器未連線，1 秒後重試");
                }
            }
            finally
            {
                if (stream != null && subscribed)
                {
                    try
                    {
                        WriteFrame(stream, StreamOff);
                    }
                    catch (IOException)
                    {
                    }
                }

                if (client != null && Interlocked.CompareExchange(ref _client, null, client) == client)
                {
                    client.Dispose();
                }
            }

            attempts++;
            if (token.WaitHandle.WaitOne(1000))
            {
                break;
            }
        }
    }

    private bool TryCreateFrame(byte[] payload, out LidarFrame? frame)
    {
        frame = null;
        if (!StartsWithScan(payload))
        {
            return false;
        }

        if (!TryReadDistances(payload, out var startDeg, out var stepDeg, out var distances))
        {
            return false;
        }

        var number = Interlocked.Increment(ref _frameNumber);
        frame = new LidarFrame(number, -startDeg, stepDeg, distances);
        return true;
    }

    private static bool TryReadDistances(byte[] payload, out double startDeg, out double stepDeg, out int[] distances)
    {
        startDeg = 0;
        stepDeg = 0;
        distances = [];
        if (payload.Length < 16 + 38)
        {
            return false;
        }

        if (ReadU16(payload, 16) != 1)
        {
            return false;
        }

        var encoderCount = ReadU16(payload, 16 + 36);
        if (encoderCount > 16)
        {
            return false;
        }

        var offset = 16 + 38 + encoderCount * 6;
        for (var pass = 0; pass < 2; pass++)
        {
            var width = pass == 0 ? 2 : 1;
            if (offset + 2 > payload.Length)
            {
                return false;
            }

            var count = ReadU16(payload, offset);
            offset += 2;
            if (count > 32)
            {
                return false;
            }

            for (var channel = 0; channel < count; channel++)
            {
                if (offset + 21 > payload.Length)
                {
                    return false;
                }

                var name = Encoding.ASCII.GetString(payload, offset, 5);
                var scale = ReadF32(payload, offset + 5);
                var bias = ReadF32(payload, offset + 9);
                var start = ReadI32(payload, offset + 13);
                var step = ReadU16(payload, offset + 17);
                var size = ReadU16(payload, offset + 19);
                offset += 21;
                if (size > 16384 || offset + size * width > payload.Length)
                {
                    return false;
                }

                if (name == "DIST1" && scale > 0 && step > 0)
                {
                    distances = new int[size];
                    for (var i = 0; i < size; i++)
                    {
                        var raw = width == 2 ? ReadU16(payload, offset + i * width) : payload[offset + i];
                        var distance = raw * scale + bias;
                        distances[i] = raw >= 16 && distance > 1 ? (int)Math.Round(distance) : 0;
                    }

                    startDeg = start / 10000.0;
                    stepDeg = step / 10000.0;
                }

                offset += size * width;
            }
        }

        return distances.Length > 1 && stepDeg > 0;
    }

    private static List<byte[]> TakePayloads(List<byte> pending)
    {
        var frames = new List<byte[]>();
        while (pending.Count >= 8)
        {
            var start = IndexOfMagic(pending);
            if (start < 0)
            {
                if (pending.Count > 3)
                {
                    pending.RemoveRange(0, pending.Count - 3);
                }

                break;
            }

            if (start > 0)
            {
                pending.RemoveRange(0, start);
            }

            if (pending.Count < 8)
            {
                break;
            }

            var size = ReadI32(pending, 4);
            if (size < 1 || size > MaxPayload)
            {
                pending.RemoveRange(0, 4);
                continue;
            }

            var end = size + 9;
            if (pending.Count < end)
            {
                break;
            }

            var payload = new byte[size];
            for (var i = 0; i < size; i++)
            {
                payload[i] = pending[8 + i];
            }

            var checksum = pending[end - 1];
            pending.RemoveRange(0, end);
            if (Xor(payload) == checksum)
            {
                frames.Add(payload);
            }
        }

        return frames;
    }

    private static void WriteFrame(NetworkStream stream, byte[] payload)
    {
        var frame = new byte[8 + payload.Length + 1];
        Magic.CopyTo(frame, 0);
        WriteI32(frame, 4, payload.Length);
        payload.CopyTo(frame, 8);
        frame[frame.Length - 1] = Xor(payload);
        stream.Write(frame, 0, frame.Length);
    }

    private static bool StartsWithScan(byte[] payload)
    {
        var sn = Encoding.ASCII.GetBytes("sSN LMDscandata ");
        var ra = Encoding.ASCII.GetBytes("sRA LMDscandata ");
        return StartsWith(payload, sn) || StartsWith(payload, ra);
    }

    private static bool StartsWith(byte[] payload, byte[] prefix)
    {
        if (payload.Length < prefix.Length)
        {
            return false;
        }

        for (var i = 0; i < prefix.Length; i++)
        {
            if (payload[i] != prefix[i])
            {
                return false;
            }
        }

        return true;
    }

    private static int IndexOfMagic(List<byte> pending)
    {
        for (var i = 0; i <= pending.Count - 4; i++)
        {
            if (pending[i] == 2 && pending[i + 1] == 2 && pending[i + 2] == 2 && pending[i + 3] == 2)
            {
                return i;
            }
        }

        return -1;
    }

    private static byte Xor(byte[] payload)
    {
        byte value = 0;
        for (var i = 0; i < payload.Length; i++)
        {
            value ^= payload[i];
        }

        return value;
    }

    private static int ReadU16(IReadOnlyList<byte> data, int offset)
    {
        return (data[offset] << 8) | data[offset + 1];
    }

    private static int ReadI32(IReadOnlyList<byte> data, int offset)
    {
        return (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];
    }

    private static void WriteI32(byte[] data, int offset, int value)
    {
        data[offset] = (byte)(value >> 24);
        data[offset + 1] = (byte)(value >> 16);
        data[offset + 2] = (byte)(value >> 8);
        data[offset + 3] = (byte)value;
    }

    private static float ReadF32(byte[] data, int offset)
    {
        Span<byte> raw = stackalloc byte[4];
        raw[0] = data[offset + 3];
        raw[1] = data[offset + 2];
        raw[2] = data[offset + 1];
        raw[3] = data[offset];
        return BitConverter.ToSingle(raw);
    }

    private void RaiseStatus(string text)
    {
        StatusChanged?.Invoke(this, text);
    }
}
