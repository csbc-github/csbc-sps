using System.Diagnostics;
using ThreeDScan.Infrastructure.Export;
using ThreeDScan.Infrastructure.Export.Models;

namespace ThreeDScan.Infrastructure.Scanners;

internal sealed record HoleCaptureOptions(string Ip, ushort Port, byte? ProgramNo = null,
    double TimeoutSeconds = 30, double PollSeconds = .5);

internal sealed record HoleCaptureReadResult(int ReturnCode, HoleNativeHeightResponse Response, HeightFrame? Frame);

/// <summary>Injectable acquisition boundary; tests never load or operate the native device.</summary>
internal interface IHoleCaptureSdk
{
    int Initialize();
    int Open(ref HoleNativeEthernetConfig config);
    int GetActiveProgram(ref byte programNo);
    int ChangeActiveProgram(byte programNo);
    int Trigger();
    HoleCaptureReadResult Read(HoleNativeHeightRequest request);
    int Close();
}

/// <summary>OneClick's native single-image protocol, isolated from surface/instrument capture.</summary>
internal sealed class HoleCaptureAcquisition(IHoleCaptureSdk sdk)
{
    internal const ushort RequestedProfiles = 1000;

    internal HeightFrame Capture(HoleCaptureOptions options, CancellationToken ct = default, Action<string>? log = null)
    {
        if (!double.IsFinite(options.TimeoutSeconds) || options.TimeoutSeconds <= 0
            || !double.IsFinite(options.PollSeconds) || options.PollSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(options));
        var config = HoleNativeEthernetConfig.Create(options.Ip, options.Port);
        bool connected = false;
        ct.ThrowIfCancellationRequested();
        try
        {
            Check(sdk.Initialize(), "Initialize", log);
            Check(sdk.Open(ref config), "EthernetOpen", log);
            connected = true;
            if (options.ProgramNo is byte requested)
            {
                byte active = 0;
                Check(sdk.GetActiveProgram(ref active), "GetActiveProgram", log);
                if (active != requested)
                {
                    Check(sdk.ChangeActiveProgram(requested), "ChangeActiveProgram", log);
                    Wait(.2, ct);
                    Check(sdk.GetActiveProgram(ref active), "GetActiveProgram", log);
                    if (active != requested) throw new InvalidDataException($"掃描器未切到程式 {requested}，目前為 {active}。");
                }
            }

            ct.ThrowIfCancellationRequested();
            var before = sdk.Read(new() { ProfileCount = 1 });
            if (!IsTransient(before.ReturnCode)) Check(before.ReturnCode, "Read previous image", log);
            uint? previousNo = before.ReturnCode == 0 ? before.Response.CurrentImageNo : null;
            log?.Invoke($"上次影像編號：{previousNo?.ToString() ?? "無"}");
            Check(sdk.Trigger(), "Trigger", log);

            var timer = Stopwatch.StartNew();
            HoleCaptureReadResult? last = null;
            while (timer.Elapsed.TotalSeconds < options.TimeoutSeconds)
            {
                Wait(options.PollSeconds, ct);
                var read = sdk.Read(new() { ProfileCount = RequestedProfiles });
                last = read;
                log?.Invoke($"取得高度影像：rc=0x{read.ReturnCode:X4}，影像={read.Response.ImageNo}，"
                    + $"輪廓={read.Response.ProfileCount}/{RequestedProfiles}，commit={read.Response.CurrentImageCommitted}");
                if (!IsTransient(read.ReturnCode)) Check(read.ReturnCode, "GetHeightImageSimpleArray", log);
                if (read.ReturnCode != 0 || read.Frame is null || read.Frame.ProfileCount == 0) continue;
                if (previousNo.HasValue && read.Frame.ImageNo == previousNo.Value) continue;
                if (read.Frame.FirstProfileNo != 0)
                    throw new InvalidDataException("高度影像缺少起始輪廓，無法完整匯出。");
                int expected = (int)Math.Min(RequestedProfiles, read.Response.ImageProfileCount);
                if (expected < read.Frame.ProfileCount)
                    throw new InvalidDataException("SDK 回傳的輪廓數量不一致。");
                var frame = CompleteFrame(read.Frame, expected, options, timer, ct, log);
                HeightFramePlyExporter.Validate(frame);
                log?.Invoke($"高度影像完成：{frame.Info.XPointCount} × {frame.ProfileCount}，影像編號={frame.ImageNo}");
                return frame;
            }
            throw new TimeoutException($"等候新的高度影像逾時（{options.TimeoutSeconds:g} 秒）；"
                + $"最後 rc=0x{last?.ReturnCode ?? 0:X4}，輪廓={last?.Response.ProfileCount ?? 0}。");
        }
        finally
        {
            if (connected)
            {
                // Finalize belongs to Ljs8Runtime, shared with surface/instrument capture.
                try
                {
                    int rc = sdk.Close();
                    log?.Invoke($"CommunicationClose: 0x{rc:X4}");
                }
                catch (Exception ex) { log?.Invoke($"CommunicationClose: {ex.Message}"); }
            }
        }
    }

    private HeightFrame CompleteFrame(HeightFrame first, int expected, HoleCaptureOptions options,
        Stopwatch timer, CancellationToken ct, Action<string>? log)
    {
        HeightFramePlyExporter.Validate(first);
        if (first.ProfileCount == expected) return first;
        var heights = new ushort[checked(first.Info.XPointCount * expected)];
        var luminance = first.Luminance is null ? null : new byte[heights.Length];
        first.Heights.CopyTo(heights, 0);
        first.Luminance?.CopyTo(luminance!, 0);
        int count = first.ProfileCount;
        while (count < expected)
        {
            ct.ThrowIfCancellationRequested();
            if (timer.Elapsed.TotalSeconds >= options.TimeoutSeconds)
                throw new TimeoutException("分批取得高度影像逾時。");
            var next = sdk.Read(new() { PositionMode = 2, ImageNo = first.ImageNo,
                ProfileNo = (uint)count, ProfileCount = (ushort)(expected - count) });
            Check(next.ReturnCode, "Get remaining profiles", log);
            var part = next.Frame ?? throw new InvalidDataException("分批取得輪廓時回傳空資料。");
            if (part.ImageNo != first.ImageNo || part.FirstProfileNo != count
                || part.ProfileCount <= 0 || part.ProfileCount > expected - count
                || !SameInfo(part.Info, first.Info) || (part.Luminance is null) != (luminance is null))
                throw new InvalidDataException("分批輪廓的影像編號、順序或量測設定不一致。");
            HeightFramePlyExporter.Validate(part);
            part.Heights.CopyTo(heights, count * first.Info.XPointCount);
            part.Luminance?.CopyTo(luminance!, count * first.Info.XPointCount);
            count += part.ProfileCount;
        }
        return new HeightFrame { Info = first.Info, ImageNo = first.ImageNo, FirstProfileNo = first.FirstProfileNo,
            ProfileCount = count, Heights = heights, Luminance = luminance };
    }

    private static bool SameInfo(HeightInfo a, HeightInfo b) => a.XPointCount == b.XPointCount
        && a.YLineCount == b.YLineCount && a.LuminanceOutput == b.LuminanceOutput
        && a.XStart == b.XStart && a.YStart == b.YStart
        && a.PitchX == b.PitchX && a.PitchY == b.PitchY && a.PitchZ == b.PitchZ;

    private static bool IsTransient(int rc) => rc is 0 or 0x8048 or 0x80A0;

    private static void Wait(double seconds, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (seconds > 0 && ct.WaitHandle.WaitOne(TimeSpan.FromSeconds(seconds))) ct.ThrowIfCancellationRequested();
    }

    private static void Check(int rc, string operation, Action<string>? log)
    {
        log?.Invoke($"{operation}: 0x{rc:X4}");
        if (rc != 0) throw new IOException($"{operation} 失敗：0x{rc:X4}");
    }
}
