using System.Diagnostics;
using System.Globalization;

namespace ThreeDScan.Infrastructure.LineScan.Hank;

public sealed record EncoderStatus(bool Available, string Message, int EncoderCount = 0,
    uint? RawPosition = null, ushort? Reserved = null, int? Delta = null,
    long RelativeCounts = 0, double Turns = 0, double? Rpm = null, double? TravelMm = null);

/// <summary>Tracks one encoder from every received scan; all resets are local.</summary>
public sealed class EncoderMonitor
{
    readonly object sync = new();
    EncoderTracker tracker = new();
    double lastReceived;
    EncoderStatus current = new(false, "先連線，再轉動 Encoder");
    public EncoderSettings Settings { get { lock (sync) return tracker.Settings; } }
    public static double Now => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
    public void Configure(int channelIndex, int decodeMultiplier, double? circumferenceMm, int ppr = 1024, bool reverse = false)
    {
        var settings = new EncoderSettings(ppr, decodeMultiplier, channelIndex + 1, circumferenceMm, reverse); settings.Validate();
        lock (sync) { tracker = new(settings); current = new(false, "設定已套用；等待新的 Encoder 封包"); }
    }
    public void Reset(string message) { lock (sync) { tracker.Reset(); current = new(false, message); } }
    public void Zero() { Reset("已在電腦端歸零；等待新基準"); }
    public void Add(Scan scan, double now)
    {
        lock (sync)
        {
            if (tracker.Latest != null && (now < lastReceived || now - lastReceived > 1)) tracker.Reset();
            try
            {
                var r = tracker.Update(scan);
                current = new(true, r.Rebased ? "已建立新基準" : "計數接收中", scan.Encoders.Count,
                    r.Position, r.Reserved, checked((int)Math.Clamp(r.Delta, int.MinValue, int.MaxValue)), r.RelativeCounts, r.Turns, r.Rpm, r.TravelMm);
                lastReceived = now;
            }
            catch (ArgumentException e) { current = new(false, e.Message, scan.Encoders.Count); }
        }
    }
    public EncoderStatus Snapshot(double now)
    {
        lock (sync) { if (tracker.Latest != null && now - lastReceived > 1) Reset("超過 1 秒未收到新資料；等待重新建立基準"); return current; }
    }
}
