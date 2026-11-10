namespace ThreeDScan.Infrastructure.LineScan.Hank;

public record EncoderSettings(int Ppr = 1024, int Multiplier = 1, int Channel = 1,
    double? WheelCircumferenceMm = null, bool Reverse = false)
{
    public long CountsPerRevolution => (long)Ppr * Multiplier;
    public void Validate()
    {
        if (Ppr <= 0 || Multiplier is not (1 or 2 or 4) || Channel < 1 || Channel > 16 ||
            WheelCircumferenceMm.HasValue && (!double.IsFinite(WheelCircumferenceMm.Value) || WheelCircumferenceMm <= 0))
            throw new ArgumentException("Encoder 每轉脈波、倍率、通道或輪周長無效");
    }
}

public record TrackedEncoder(ushort Counter, uint TimestampUs, uint Position, ushort Reserved,
    long Delta, long RelativeCounts, double Turns, double? Rpm, double? TravelMm, string Direction, bool Rebased);

public sealed class EncoderTracker
{
    public EncoderSettings Settings { get; }
    public TrackedEncoder? Latest { get; private set; }
    Scan? previous;
    long relative;
    public EncoderTracker(EncoderSettings? settings = null) { Settings = settings ?? new(); Settings.Validate(); }
    public void Reset() { previous = null; Latest = null; relative = 0; }
    public TrackedEncoder Update(Scan scan)
    {
        if (scan.Status != 0) { Reset(); throw new ArgumentException($"掃描器回報異常狀態 {scan.Status}，請先排除異常"); }
        if (scan.Encoders.Count < Settings.Channel) { Reset(); throw new ArgumentException($"封包未輸出 Encoder 通道 {Settings.Channel}；請確認接線、輸入模式及 Encoder 資料輸出設定"); }
        var encoder = scan.Encoders[Settings.Channel - 1];
        long delta = 0; double? rpm = null; bool rebased = previous == null;
        if (previous != null)
        {
            if (previous.Counter == scan.Counter && previous.Timestamp == scan.Timestamp) return Latest!;
            uint elapsed = unchecked(scan.Timestamp - previous.Timestamp);
            int sequence = (scan.Counter - previous.Counter) & 65535;
            if (elapsed == 0 || elapsed > 2000000 || sequence <= 0 || sequence >= 0x8000) { relative = 0; rebased = true; }
            else
            {
                uint raw = unchecked(encoder.Position - previous.Encoders[Settings.Channel - 1].Position);
                if (raw == 0x80000000) { Reset(); throw new ArgumentException("計數跳變超過可判定範圍，請重新歸零並檢查連線"); }
                delta = raw < 0x80000000 ? raw : (long)raw - 0x100000000;
                if (Settings.Reverse) delta = -delta;
                relative = checked(relative + delta);
                rpm = delta * 60000000.0 / elapsed / Settings.CountsPerRevolution;
            }
        }
        previous = scan;
        double turns = relative / (double)Settings.CountsPerRevolution;
        return Latest = new(scan.Counter, scan.Timestamp, encoder.Position, encoder.Reserved, delta, relative, turns, rpm,
            Settings.WheelCircumferenceMm.HasValue ? turns * Settings.WheelCircumferenceMm : null,
            delta > 0 ? "正向（+）" : delta < 0 ? "反向（-）" : "停止／未轉動", rebased);
    }
}
