using System.Text.Json;
using ThreeDScan.Domain.Abstractions;
using ThreeDScan.Domain.Models;

namespace ThreeDScan.Infrastructure.LineScan;

/// <summary>
/// 逐截面沿 X 空隙分段，再把相鄰截面重疊的段串成同一件。量測前先剔除離群並平均連續圈。
/// 只有高過門檻的點參與分段；量測時每個截面從最高點往兩側走到落回當地滾輪面取翼尖，
/// 底寬、高度與翼長取中段截面的中位數。長度是整支的行程。
/// </summary>
public sealed class CSharpLineMeasureService : ICSharpLineMeasure
{
    /// <summary>投影後保留的最小沿滾輪 X (mm)。與 Hank 重建預設 xmin 相同。</summary>
    private const double AlongXMinMm = -1000;

    /// <summary>投影後保留的最大沿滾輪 X (mm)。與 Hank 重建預設 xmax 相同。</summary>
    private const double AlongXMaxMm = 1000;

    /// <summary>投影後保留的最大高度 (mm)。與 Hank 重建預設 max_height 相同。</summary>
    private const double MaxHeightMm = 500;

    /// <summary>量測時沿 X 往兩側多取的範圍 (mm)。用來取得翼尖兩側的滾輪面。</summary>
    private const double BandMarginMm = 40;

    /// <summary>
    /// 高出當地滾輪面不超過此值即視為翼尖落地 (mm)。
    /// 對五份錄製掃過 0.5～3.0，1.0 在底寬與翼長之間最平衡：再小翼尖會踩到雜訊跑到滾輪面上。
    /// </summary>
    private const double ToeMarginMm = 1.0;

    /// <summary>估當地滾輪面時，截面兩端各取的點數。</summary>
    private const int FloorEdgePoints = 8;

    /// <summary>可用截面的最少點數。太疏的截面走不出翼尖。</summary>
    private const int MinSectionPoints = 30;

    /// <summary>串接時容許中間漏掉的截面數。偶爾會有一兩個截面點太疏，分不出段。</summary>
    private const int TrackSkipSections = 3;

    /// <inheritdoc />
    public IReadOnlyList<MeasuredAngleIron> Measure(
        string sessionDirectory,
        RollerBaselineRecord baseline,
        double rollerSpeedMmPerSec,
        double gapMm,
        double minHeightMm,
        LineScanRegion? region,
        int averageScans,
        double cropStartSeconds,
        double? cropEndSeconds)
    {
        if (averageScans < 1 || averageScans > 600)
        {
            throw new InvalidOperationException("LineScan:AverageScans 必須是 1～600。");
        }

        if (!double.IsFinite(cropStartSeconds) || cropStartSeconds < 0)
        {
            throw new InvalidOperationException("LineScan:CropStartSeconds 必須是大於等於 0 的有限數值。");
        }

        if (cropEndSeconds is double endSeconds && (!double.IsFinite(endSeconds) || endSeconds <= cropStartSeconds))
        {
            throw new InvalidOperationException("LineScan:CropEndSeconds 必須大於 CropStartSeconds。");
        }

        var samples = ReadSamples(
            sessionDirectory,
            baseline,
            rollerSpeedMmPerSec,
            region,
            averageScans,
            cropStartSeconds,
            cropEndSeconds);
        if (samples.Count == 0)
        {
            return Array.Empty<MeasuredAngleIron>();
        }

        var parts = new List<MeasuredAngleIron>();
        foreach (var track in BuildTracks(samples, gapMm, minHeightMm))
        {
            var part = ToPart(parts.Count + 1, track);
            if (part is not null)
            {
                parts.Add(part);
            }
        }

        return parts;
    }

    /// <summary>
    /// 逐截面分段再跨截面串接，回傳由左到右排好的每一件。
    /// 角鋼在輸送中會橫向偏移，整支的 X 投影會互相重疊，所以分件不能看整支的範圍。
    /// </summary>
    private static List<Track> BuildTracks(List<Sample> samples, double gapMm, double minHeightMm)
    {
        var sections = BuildSections(samples);
        var sectionRuns = new List<List<SectionRun>>(sections.Count);
        foreach (var section in sections)
        {
            sectionRuns.Add(SplitRuns(section, gapMm, minHeightMm));
        }

        LinkRuns(sectionRuns, gapMm);
        var tracks = new Dictionary<SectionRun, Track>();
        var order = new List<Track>();
        for (var index = 0; index < sections.Count; index++)
        {
            var spans = MergeRunsByPart(sectionRuns[index]);
            ApplyRunWindows(spans);
            foreach (var span in spans)
            {
                var root = FindRoot(span);
                if (!tracks.TryGetValue(root, out var track))
                {
                    track = new Track();
                    tracks[root] = track;
                    order.Add(track);
                }

                track.CenterSum += (span.Low + span.High) / 2.0;
                track.CenterCount++;
                var profile = MeasureSection(sections[index], span.WindowLow, span.WindowHigh, minHeightMm);
                if (profile is not null)
                {
                    track.Profiles.Add(profile);
                }
            }
        }

        order.Sort(CompareTrackCenter);
        return order;
    }

    /// <summary>把點依行程分成截面。截面之間依行程排好，截面內沿 X 排好。</summary>
    private static List<List<Sample>> BuildSections(List<Sample> samples)
    {
        var grouped = new Dictionary<double, List<Sample>>();
        foreach (var sample in samples)
        {
            if (!grouped.TryGetValue(sample.Y, out var section))
            {
                section = new List<Sample>();
                grouped[sample.Y] = section;
            }

            section.Add(sample);
        }

        var sections = new List<List<Sample>>(grouped.Count);
        foreach (var section in grouped.Values)
        {
            section.Sort(CompareAlongX);
            sections.Add(section);
        }

        sections.Sort(CompareSectionAlongY);
        return sections;
    }

    /// <summary>
    /// 在一個截面裡沿 X 分段，回傳每段立起來的 X 範圍。
    /// 只有高過門檻的點參與，貼著滾輪的翼尖與滾輪面不會把相鄰兩件接起來。
    /// </summary>
    private static List<SectionRun> SplitRuns(List<Sample> section, double gapMm, double minHeightMm)
    {
        var runs = new List<SectionRun>();
        SectionRun? current = null;
        foreach (var sample in section)
        {
            if (sample.Z < minHeightMm)
            {
                continue;
            }

            if (current is null || sample.X - current.High > gapMm)
            {
                current = new SectionRun();
                current.Low = sample.X;
                current.High = sample.X;
                runs.Add(current);
                continue;
            }

            current.High = sample.X;
        }

        return runs;
    }

    /// <summary>
    /// 決定每一段取點的範圍：往外最多 <see cref="BandMarginMm" />，但不越過與相鄰段的中線。
    /// 這樣估當地滾輪面時不會踩到隔壁角鋼的斜面。
    /// </summary>
    private static void ApplyRunWindows(List<SectionRun> runs)
    {
        for (var i = 0; i < runs.Count; i++)
        {
            var run = runs[i];
            run.WindowLow = run.Low - BandMarginMm;
            run.WindowHigh = run.High + BandMarginMm;
            if (i > 0)
            {
                var middle = (runs[i - 1].High + run.Low) / 2.0;
                if (middle > run.WindowLow)
                {
                    run.WindowLow = middle;
                }
            }

            if (i + 1 < runs.Count)
            {
                var middle = (run.High + runs[i + 1].Low) / 2.0;
                if (middle < run.WindowHigh)
                {
                    run.WindowHigh = middle;
                }
            }
        }
    }

    /// <summary>
    /// 把相鄰截面上 X 範圍相接的段接成同一件。
    /// 相鄰截面之間的偏移只有一兩 mm，所以相接就足以判定是同一件；
    /// 往前多比幾個截面，是為了容許中間有幾個截面分不出段。
    /// </summary>
    private static void LinkRuns(List<List<SectionRun>> sectionRuns, double gapMm)
    {
        for (var index = 1; index < sectionRuns.Count; index++)
        {
            var from = Math.Max(0, index - TrackSkipSections - 1);
            foreach (var run in sectionRuns[index])
            {
                for (var previous = index - 1; previous >= from; previous--)
                {
                    foreach (var earlier in sectionRuns[previous])
                    {
                        var overlap = Math.Min(earlier.High, run.High) - Math.Max(earlier.Low, run.Low);
                        if (overlap >= -gapMm)
                        {
                            Union(earlier, run);
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// 把同一件在這個截面上的段併成一段，回傳沿 X 排好的結果。
    /// 角鋼中間漏點時會在截面裡裂成兩段，併起來才量得到完整剖面。
    /// </summary>
    private static List<SectionRun> MergeRunsByPart(List<SectionRun> runs)
    {
        var spans = new List<SectionRun>();
        var byRoot = new Dictionary<SectionRun, SectionRun>();
        foreach (var run in runs)
        {
            var root = FindRoot(run);
            if (byRoot.TryGetValue(root, out var span))
            {
                if (run.Low < span.Low)
                {
                    span.Low = run.Low;
                }

                if (run.High > span.High)
                {
                    span.High = run.High;
                }

                continue;
            }

            span = new SectionRun();
            span.Low = run.Low;
            span.High = run.High;
            span.Parent = root;
            byRoot[root] = span;
            spans.Add(span);
        }

        spans.Sort(CompareRunAlongX);
        return spans;
    }

    /// <summary>找出這一段所屬的件，順手壓平指向。</summary>
    private static SectionRun FindRoot(SectionRun run)
    {
        var root = run;
        while (root.Parent is not null)
        {
            root = root.Parent;
        }

        var node = run;
        while (node.Parent is not null)
        {
            var next = node.Parent;
            node.Parent = root;
            node = next;
        }

        return root;
    }

    /// <summary>把兩段歸到同一件。</summary>
    private static void Union(SectionRun left, SectionRun right)
    {
        var leftRoot = FindRoot(left);
        var rightRoot = FindRoot(right);
        if (!ReferenceEquals(leftRoot, rightRoot))
        {
            rightRoot.Parent = leftRoot;
        }
    }

    private static List<Sample> ReadSamples(
        string sessionDirectory,
        RollerBaselineRecord baseline,
        double rollerSpeedMmPerSec,
        LineScanRegion? region,
        int averageScans,
        double cropStartSeconds,
        double? cropEndSeconds)
    {
        var path = Path.Combine(sessionDirectory, "frames.jsonl");
        if (!File.Exists(path))
        {
            throw new InvalidOperationException("找不到線掃描截面：" + path);
        }

        var samples = new List<Sample>();
        double? firstTime = null;
        double? previousTime = null;
        long? previousCounter = null;
        long? previousDeviceUs = null;
        double duration = 0;
        var block = new List<List<(int Index, double X, double Y)>>();
        var times = new List<double>();
        var averager = new LineScanFrameAverager();
        using var stream = File.OpenRead(path);
        using var reader = new StreamReader(stream);
        while (true)
        {
            var line = reader.ReadLine();
            if (line is null)
            {
                break;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            var time = root.GetProperty("t").GetDouble();
            if (firstTime is null)
            {
                firstTime = time;
            }

            var elapsed = time - firstTime.Value;
            if (elapsed > duration)
            {
                duration = elapsed;
            }

            if (elapsed < cropStartSeconds || (cropEndSeconds is double cropEnd && elapsed > cropEnd))
            {
                continue;
            }

            var frame = ReadFrameRays(root);
            var hasCounter = TryReadInt64(root, "counter", out var counter);
            var hasDeviceUs = TryReadInt64(root, "timestampUs", out var deviceUs);
            if (hasCounter && previousCounter is long priorCounter)
            {
                var sameStamp = hasDeviceUs && previousDeviceUs is long priorUs && deviceUs == priorUs;
                if (counter == priorCounter && sameStamp)
                {
                    continue;
                }

                if (averageScans != 1 && ((counter - priorCounter) & 0xFFFF) != 1)
                {
                    FlushBlock(samples, block, times, averager, firstTime.Value, baseline, rollerSpeedMmPerSec, region);
                }
            }
            else if (!hasCounter && averageScans != 1 && previousTime is double prior && (time < prior || time - prior >= 1))
            {
                FlushBlock(samples, block, times, averager, firstTime.Value, baseline, rollerSpeedMmPerSec, region);
            }

            if (hasCounter)
            {
                previousCounter = counter;
            }

            if (hasDeviceUs)
            {
                previousDeviceUs = deviceUs;
            }

            previousTime = time;
            if (averageScans == 1)
            {
                var raw = new List<(double X, double Y)>(frame.Count);
                foreach (var ray in frame)
                {
                    raw.Add((ray.X, ray.Y));
                }

                AddProjected(samples, raw, time, firstTime.Value, baseline, rollerSpeedMmPerSec, region);
                continue;
            }

            block.Add(frame);
            times.Add(time);
            if (block.Count == averageScans)
            {
                FlushBlock(samples, block, times, averager, firstTime.Value, baseline, rollerSpeedMmPerSec, region);
            }
        }

        FlushBlock(samples, block, times, averager, firstTime ?? 0, baseline, rollerSpeedMmPerSec, region);
        if (cropEndSeconds is double configuredEnd && configuredEnd > duration + 1e-9)
        {
            throw new InvalidOperationException(
                "裁切時間必須在錄製範圍 0～" + duration.ToString("0.######") + " 秒內。");
        }

        var windowEnd = cropEndSeconds ?? duration;
        if (cropStartSeconds > 0 && !(cropStartSeconds < windowEnd))
        {
            throw new InvalidOperationException(
                "裁切時間必須在錄製範圍 0～" + duration.ToString("0.######") + " 秒內。");
        }

        return samples;
    }

    /// <summary>把累積的圈平均成一圈，再投影進點列。</summary>
    private static void FlushBlock(
        List<Sample> samples,
        List<List<(int Index, double X, double Y)>> block,
        List<double> times,
        LineScanFrameAverager averager,
        double firstTime,
        RollerBaselineRecord baseline,
        double rollerSpeedMmPerSec,
        LineScanRegion? region)
    {
        if (times.Count == 0)
        {
            return;
        }

        var elapsed = 0.0;
        foreach (var time in times)
        {
            elapsed += time;
        }

        elapsed /= times.Count;
        foreach (var frame in block)
        {
            averager.Add(frame);
        }

        var points = averager.Take();
        AddProjected(samples, points, elapsed, firstTime, baseline, rollerSpeedMmPerSec, region);
        block.Clear();
        times.Clear();
    }

    /// <summary>
    /// 套用框選的 X 範圍，並只留下沿滾輪 X 在 -1000～1000 mm、高度不超過 500 mm 的點。
    /// 框選不限制高度，高度由滾輪校正基準決定，否則框上緣會把較高角鋼的頂端切掉。
    /// 也不設高度下限，貼著滾輪的翼尖與滾輪面都要留著，量測時才取得到當地基準。
    /// </summary>
    private static void AddProjected(
        List<Sample> samples,
        IReadOnlyList<(double X, double Y)> points,
        double time,
        double firstTime,
        RollerBaselineRecord baseline,
        double rollerSpeedMmPerSec,
        LineScanRegion? region)
    {
        var travel = (time - firstTime) * rollerSpeedMmPerSec;
        foreach (var (sensorX, sensorY) in points)
        {
            if (region is not null && !region.ContainsAlongX(sensorX))
            {
                continue;
            }

            Project(sensorX, sensorY, baseline.LineA, baseline.LineB, baseline.LineC, out var along, out var height);
            if (along < AlongXMinMm || along > AlongXMaxMm || height > MaxHeightMm)
            {
                continue;
            }

            samples.Add(new Sample
            {
                X = along,
                Y = travel,
                Z = height,
            });
        }
    }

    /// <summary>
    /// 把一件收集到的剖面收成尺寸。底寬、高度與翼長取中段截面的中位數，頭尾角點取第一與最後一個可用截面。
    /// 沒有任何可用截面時回傳 null，該件不計入。
    /// </summary>
    private static MeasuredAngleIron? ToPart(int index, Track track)
    {
        var profiles = track.Profiles;
        if (profiles.Count == 0)
        {
            return null;
        }

        profiles.Sort(CompareProfileAlongY);
        var head = profiles[0];
        var tail = profiles[profiles.Count - 1];
        var middle = MiddleProfiles(profiles);
        return new MeasuredAngleIron
        {
            Index = index,
            HeadLeft = ToPoint(head.Left),
            HeadRight = ToPoint(head.Right),
            HeadApex = ToPoint(head.Apex),
            TailLeft = ToPoint(tail.Left),
            TailRight = ToPoint(tail.Right),
            TailApex = ToPoint(tail.Apex),
            Width = Median(middle, profile => profile.Base),
            Height = Median(middle, profile => profile.Height),
            Length = tail.Y - head.Y,
            LeftWidth = Median(middle, profile => profile.LeftLeg),
            RightWidth = Median(middle, profile => profile.RightLeg),
            LeftLength = Distance(head.Left, tail.Left),
            RightLength = Distance(head.Right, tail.Right),
        };
    }

    /// <summary>
    /// 量一件在某個截面上的剖面：只取這一段的範圍，從最高點往兩側走到落回當地滾輪面得到兩個翼尖。
    /// 高度是最高點扣掉當地滾輪面，不用基準的絕對值，滾輪面有高低差也量得準。
    /// 點太疏、立起高度不足或走不出翼尖的截面不採用。
    /// </summary>
    private static SectionProfile? MeasureSection(
        List<Sample> section,
        double windowLow,
        double windowHigh,
        double minHeightMm)
    {
        var window = TakeWindow(section, windowLow, windowHigh);
        if (window.Count < MinSectionPoints)
        {
            return null;
        }

        var floor = EdgeFloor(window);
        var apex = 0;
        for (var i = 1; i < window.Count; i++)
        {
            if (window[i].Z > window[apex].Z)
            {
                apex = i;
            }
        }

        var height = window[apex].Z - floor.At(window[apex].X);
        if (height < minHeightMm)
        {
            return null;
        }

        var left = -1;
        for (var i = apex - 1; i >= 0; i--)
        {
            if (window[i].Z <= floor.At(window[i].X) + ToeMarginMm)
            {
                left = i;
                break;
            }
        }

        var right = -1;
        for (var i = apex + 1; i < window.Count; i++)
        {
            if (window[i].Z <= floor.At(window[i].X) + ToeMarginMm)
            {
                right = i;
                break;
            }
        }

        if (left < 0 || right < 0)
        {
            return null;
        }

        var profile = new SectionProfile();
        profile.Y = window[apex].Y;
        profile.Left = window[left];
        profile.Right = window[right];
        profile.Apex = window[apex];
        profile.Base = window[right].X - window[left].X;
        profile.Height = height;
        profile.LeftLeg = SectionLeg(window[apex], window[left]);
        profile.RightLeg = SectionLeg(window[apex], window[right]);
        return profile;
    }

    /// <summary>取截面中落在這一段取點範圍內的點。截面已沿 X 排好，回傳的也是。</summary>
    private static List<Sample> TakeWindow(List<Sample> section, double windowLow, double windowHigh)
    {
        var window = new List<Sample>();
        foreach (var sample in section)
        {
            if (sample.X < windowLow)
            {
                continue;
            }

            if (sample.X > windowHigh)
            {
                break;
            }

            window.Add(sample);
        }

        return window;
    }

    /// <summary>
    /// 用截面兩端的點擬一條有斜度的當地滾輪面。
    /// 滾輪面相對校正基準約有 0.9 mm/100 mm 的殘餘傾斜，用單一高度會讓較低那側的翼尖提前停下。
    /// 兩端各取中位數再連線，少數離群點不會把線拉歪。
    /// </summary>
    private static FloorLine EdgeFloor(List<Sample> section)
    {
        var take = Math.Min(FloorEdgePoints, section.Count);
        var leftX = new List<double>(take);
        var leftZ = new List<double>(take);
        var rightX = new List<double>(take);
        var rightZ = new List<double>(take);
        for (var i = 0; i < take; i++)
        {
            leftX.Add(section[i].X);
            leftZ.Add(section[i].Z);
            rightX.Add(section[section.Count - 1 - i].X);
            rightZ.Add(section[section.Count - 1 - i].Z);
        }

        var line = new FloorLine();
        line.X = Median(leftX);
        line.Z = Median(leftZ);
        var farX = Median(rightX);
        var farZ = Median(rightZ);
        var span = farX - line.X;
        if (Math.Abs(span) >= 1)
        {
            line.Slope = (farZ - line.Z) / span;
        }

        return line;
    }

    /// <summary>取中段 60% 的截面。兩端是角鋼進出掃描線時的不完整剖面，尺寸偏小。</summary>
    private static List<SectionProfile> MiddleProfiles(List<SectionProfile> profiles)
    {
        var from = profiles.Count / 5;
        var to = profiles.Count * 4 / 5;
        if (to - from < 1)
        {
            return profiles;
        }

        return profiles.GetRange(from, to - from);
    }

    /// <summary>這批截面某個量的中位數 (mm)。</summary>
    private static double Median(List<SectionProfile> profiles, Func<SectionProfile, double> selector)
    {
        var values = new List<double>(profiles.Count);
        foreach (var profile in profiles)
        {
            values.Add(selector(profile));
        }

        return Median(values);
    }

    /// <summary>排序後取中位數。偶數個取中間兩個的平均。</summary>
    private static double Median(List<double> values)
    {
        values.Sort();
        var middle = values.Count / 2;
        if (values.Count % 2 == 1)
        {
            return values[middle];
        }

        return (values[middle - 1] + values[middle]) / 2.0;
    }

    /// <summary>同一截面上，頂點到這一側翼尖的距離 (mm)。</summary>
    private static double SectionLeg(Sample apex, Sample toe)
    {
        var dx = apex.X - toe.X;
        var dz = apex.Z - toe.Z;
        return Math.Sqrt(dx * dx + dz * dz);
    }

    /// <summary>兩角點的三維距離 (mm)。</summary>
    private static double Distance(Sample start, Sample end)
    {
        var dx = start.X - end.X;
        var dy = start.Y - end.Y;
        var dz = start.Z - end.Z;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    private static MeasuredPoint ToPoint(Sample sample)
    {
        return new MeasuredPoint
        {
            X = sample.X,
            Y = sample.Y,
            Z = sample.Z,
        };
    }

    private static int CompareProfileAlongY(SectionProfile left, SectionProfile right)
    {
        return left.Y.CompareTo(right.Y);
    }

    private static int CompareSectionAlongY(List<Sample> left, List<Sample> right)
    {
        return left[0].Y.CompareTo(right[0].Y);
    }

    private static int CompareRunAlongX(SectionRun left, SectionRun right)
    {
        return left.Low.CompareTo(right.Low);
    }

    private static int CompareTrackCenter(Track left, Track right)
    {
        return TrackCenter(left).CompareTo(TrackCenter(right));
    }

    /// <summary>這一件各截面中心 X 的平均 (mm)。用來把件由左到右排序。</summary>
    private static double TrackCenter(Track track)
    {
        if (track.CenterCount == 0)
        {
            return 0;
        }

        return track.CenterSum / track.CenterCount;
    }

    /// <summary>讀錄製列上的整數。沒有這個欄位或不是整數時回傳 false。</summary>
    private static bool TryReadInt64(JsonElement root, string name, out long value)
    {
        value = 0;
        if (!root.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.Number)
        {
            return false;
        }

        return property.TryGetInt64(out value);
    }

    /// <summary>讀這一圈的射線。有 rays 就用射線編號；舊檔只有 points 時依陣列順序編號。</summary>
    private static List<(int Index, double X, double Y)> ReadFrameRays(JsonElement root)
    {
        var frame = new List<(int Index, double X, double Y)>();
        if (root.TryGetProperty("rays", out var rays) && rays.ValueKind == JsonValueKind.Array)
        {
            foreach (var point in rays.EnumerateArray())
            {
                if (!TryReadRay(point, out var index, out var sensorX, out var sensorY))
                {
                    continue;
                }

                frame.Add((index, sensorX, sensorY));
            }

            return frame;
        }

        if (!root.TryGetProperty("points", out var points) || points.ValueKind != JsonValueKind.Array)
        {
            return frame;
        }

        var fallback = 0;
        foreach (var point in points.EnumerateArray())
        {
            if (point.ValueKind != JsonValueKind.Array || point.GetArrayLength() < 2)
            {
                continue;
            }

            if (point[0].ValueKind != JsonValueKind.Number || point[1].ValueKind != JsonValueKind.Number)
            {
                continue;
            }

            var sensorX = point[0].GetDouble();
            var sensorY = point[1].GetDouble();
            if (!double.IsFinite(sensorX) || !double.IsFinite(sensorY))
            {
                continue;
            }

            frame.Add((fallback, sensorX, sensorY));
            fallback++;
        }

        return frame;
    }

    /// <summary>讀一條射線。[編號, X, Y]，無效座標略過。</summary>
    private static bool TryReadRay(JsonElement point, out int index, out double sensorX, out double sensorY)
    {
        index = 0;
        sensorX = 0;
        sensorY = 0;
        if (point.ValueKind != JsonValueKind.Array || point.GetArrayLength() < 3)
        {
            return false;
        }

        if (point[0].ValueKind != JsonValueKind.Number
            || point[1].ValueKind != JsonValueKind.Number
            || point[2].ValueKind != JsonValueKind.Number)
        {
            return false;
        }

        index = point[0].GetInt32();
        sensorX = point[1].GetDouble();
        sensorY = point[2].GetDouble();
        return double.IsFinite(sensorX) && double.IsFinite(sensorY);
    }

    /// <summary>感測器平面點投影到滾輪：沿切向是 X，離開平面的高度是 Z。</summary>
    private static void Project(
        double sensorX,
        double sensorY,
        double lineA,
        double lineB,
        double lineC,
        out double along,
        out double height)
    {
        var footX = -lineC * lineA;
        var footY = -lineC * lineB;
        along = (sensorX - footX) * (-lineB) + (sensorY - footY) * lineA;
        height = lineA * sensorX + lineB * sensorY + lineC;
    }

    private static int CompareAlongX(Sample left, Sample right)
    {
        return left.X.CompareTo(right.X);
    }

    /// <summary>一個截面上的當地滾輪面，以通過 (X, Z) 的斜線表示。</summary>
    private sealed class FloorLine
    {
        /// <summary>線上參考點的 X (mm)。</summary>
        public double X;

        /// <summary>線上參考點的高度 (mm)。</summary>
        public double Z;

        /// <summary>每 mm 的高度變化。滾輪面沒傾斜時為 0。</summary>
        public double Slope;

        /// <summary>這個 X 位置的滾輪面高度 (mm)。</summary>
        public double At(double x)
        {
            return Z + Slope * (x - X);
        }
    }

    /// <summary>一個截面裡，一件角鋼立起來的那一段。</summary>
    private sealed class SectionRun
    {
        /// <summary>這一段的 X 下界 (mm)。</summary>
        public double Low;

        /// <summary>這一段的 X 上界 (mm)。</summary>
        public double High;

        /// <summary>量測取點範圍的 X 下界 (mm)。</summary>
        public double WindowLow;

        /// <summary>量測取點範圍的 X 上界 (mm)。</summary>
        public double WindowHigh;

        /// <summary>串接用的上層段。自己就是代表時為 null。</summary>
        public SectionRun? Parent;
    }

    /// <summary>跨截面串接起來的一件角鋼。</summary>
    private sealed class Track
    {
        /// <summary>各截面中心 X 的總和 (mm)。</summary>
        public double CenterSum;

        /// <summary>累計進去的截面數。</summary>
        public int CenterCount;

        /// <summary>量得出來的剖面。走不出翼尖的截面不會進來。</summary>
        public List<SectionProfile> Profiles = new List<SectionProfile>();
    }

    /// <summary>一個截面量出來的翼尖、頂點與尺寸。</summary>
    private sealed class SectionProfile
    {
        /// <summary>這個截面的行程 Y (mm)。</summary>
        public double Y;

        /// <summary>左翼尖。</summary>
        public Sample Left;

        /// <summary>右翼尖。</summary>
        public Sample Right;

        /// <summary>最高點。</summary>
        public Sample Apex;

        /// <summary>兩翼尖的 X 跨距，即底寬 (mm)。</summary>
        public double Base;

        /// <summary>最高點扣掉當地滾輪面 (mm)。</summary>
        public double Height;

        /// <summary>頂點到左翼尖的距離 (mm)。</summary>
        public double LeftLeg;

        /// <summary>頂點到右翼尖的距離 (mm)。</summary>
        public double RightLeg;
    }

    private struct Sample
    {
        public double X;
        public double Y;
        public double Z;
    }
}
