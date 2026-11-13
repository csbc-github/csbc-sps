namespace ThreeDScan.Infrastructure.LineScan.Hank;

/// <summary>Configured forward-only encoder counts derived from the sensor clock, preserving real scan geometry.</summary>
internal sealed class ForwardEncoderSimulation
{
    readonly int channel;
    readonly int direction;
    readonly double countsPerMicrosecond;
    Scan? previous;
    ulong elapsedMicroseconds;
    long previousTicks;
    uint position;

    public ForwardEncoderSimulation(ReconstructionOptions settings, double forwardSpeedMmPerSec)
    {
        double mmPerCount = 2 * Math.PI * settings.WheelRadiusMm / settings.EncoderCountsPerRev;
        if (!double.IsFinite(forwardSpeedMmPerSec) || forwardSpeedMmPerSec <= 0
            || !double.IsFinite(mmPerCount) || mmPerCount <= 0 || settings.EncoderChannel is < 1 or > 16
            || settings.Direction is not (1 or -1))
            throw new ArgumentException("模擬 Encoder 的前進速度、通道或滾輪比例無效");
        countsPerMicrosecond = forwardSpeedMmPerSec / mmPerCount / 1e6;
        if (!double.IsFinite(countsPerMicrosecond) || countsPerMicrosecond <= 0)
            throw new ArgumentException("模擬 Encoder 的速度與每計數行程無法表示");
        channel = settings.EncoderChannel;
        direction = settings.Direction;
    }

    public Scan Apply(Scan scan)
    {
        // Let the existing session tracker reject an actual scanner fault.
        if (scan.Status != 0) return scan;
        if (previous != null)
        {
            uint elapsed = unchecked(scan.Timestamp - previous.Timestamp);
            int sequence = (scan.Counter - previous.Counter) & 65535;
            if (elapsed == 0 && sequence == 0) return WithPosition(scan);
            // Match the session tracker's reset guard rather than generating motion across a reset.
            if (elapsed == 0 || elapsed > 2000000 || sequence <= 0 || sequence >= 0x8000)
                throw new ArgumentException("模擬 Encoder 的設備時間或掃描計數器中斷，請重新開始掃描");
            elapsedMicroseconds = checked(elapsedMicroseconds + elapsed);
        }
        double totalTicks = Math.Floor(elapsedMicroseconds * countsPerMicrosecond);
        if (!double.IsFinite(totalTicks) || totalTicks < 0 || totalTicks >= long.MaxValue)
            throw new ArgumentException("模擬 Encoder 的累積計數超出可表示範圍");
        long ticks = (long)totalTicks;
        if (ticks - previousTicks >= 0x80000000L)
            throw new ArgumentException("模擬 Encoder 每圈的計數增量超出可判定範圍");
        // Direction compensates the configured real-encoder sign so simulated travel is forward (+Y).
        position = unchecked((uint)(direction * ticks));
        previousTicks = ticks;
        previous = scan;
        return WithPosition(scan);
    }

    Scan WithPosition(Scan scan)
    {
        var encoders = scan.Encoders.ToList();
        while (encoders.Count < channel) encoders.Add(new EncoderReading(0, 0));
        encoders[channel - 1] = new EncoderReading(position, 0);
        return scan with { Encoders = Array.AsReadOnly(encoders.ToArray()) };
    }
}
