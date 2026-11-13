using ThreeDScan.Domain.Models;
using ThreeDScan.Infrastructure.LineScan.Hank;

namespace ThreeDScan.Infrastructure.LineScan;

/// <summary>線掃描 3D 點雲截面量測（埠自 Hank SickScanApiBackend.MeasureLine）。</summary>
public sealed class LineScanMeasureService
{
    /// <summary>從重建點雲偵測角鋼並轉成契約型別。</summary>
    public IReadOnlyList<MeasuredAngleIron> MeasureLine(Cloud cloud, Calibration calibration, CancellationToken cancellationToken)
        => MeasureLine(cloud.Points.GroupBy(point => point.Frame).OrderBy(group => group.Key)
            .Select(group => (IReadOnlyList<Vertex>)group.ToArray()), calibration, cancellationToken);

    /// <summary>逐圈讀取保留桌面背景的量測資料，不需把整份錄製載入記憶體。</summary>
    public IReadOnlyList<MeasuredAngleIron> MeasureLine(IEnumerable<IReadOnlyList<Vertex>> frames,
        Calibration calibration, CancellationToken cancellationToken)
    {
        var d = (calibration.Second - calibration.First) * (1 / calibration.First.Distance(calibration.Second));
        double flip = calibration.First.X * d.Y - calibration.First.Y * d.X >= 0 ? 1 : -1;

        P Raw(Vertex p)
        {
            return calibration.First + d * p.X + new P(-d.Y, d.X) * (p.Z * flip);
        }

        var tracks = new List<FeatureTrack>();
        foreach (var frame in frames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var points = frame.OrderBy(point => point.Ray).ToArray();
            if (points.Length < 8)
            {
                continue;
            }

            List<ProfileResult> profiles;
            try
            {
                profiles = Profile.Measure(points.Select(Raw), calibration, recoverSparse: true,
                    autoCalibrate: true, autoModel: Profile.SceneContactModel);
            }
            catch (ArgumentException)
            {
                continue;
            }

            var assigned = new HashSet<FeatureTrack>();
            foreach (var profile in profiles.OrderBy(profile => calibration.Transform((profile.LegPoints ?? profile.Points)[1]).X))
            {
                Vertex Feature(P raw)
                {
                    var level = calibration.Transform(raw);
                    return points[0] with { X = level.X, Z = level.Y };
                }

                var fitted = profile.LegPoints ?? profile.Points;
                var feature = new Features(
                    Feature(fitted[0]),
                    Feature(fitted[1]),
                    Feature(fitted[2]));
                if (feature.Width <= 0)
                {
                    continue;
                }

                var matching = tracks
                    .Where(track => Math.Abs(track.CenterX - feature.CenterX) <= Math.Max(10, Math.Min(track.Width, feature.Width) * .25))
                    .ToArray();
                if (matching.Length > 1 || matching.Length == 1 && assigned.Contains(matching[0]))
                {
                    throw new InvalidDataException("角鋼截面對應不唯一，請分別框選或確認機構沒有橫向移動。");
                }

                if (matching.Length == 0)
                {
                    var track = new FeatureTrack(feature);
                    tracks.Add(track);
                    assigned.Add(track);
                }
                else
                {
                    var track = matching[0];
                    track.Observe(feature);
                    assigned.Add(track);
                }
            }
        }

        if (tracks.Count == 0 || tracks.Any(track => track.Samples < 2 || Math.Abs(track.Tail.Y - track.Head.Y) <= 0))
        {
            throw new InvalidDataException("缺少至少兩個可對應的完整角鋼截面或有效 Encoder 行程。");
        }

        var ordered = tracks.OrderBy(track => track.CenterX).ToArray();
        for (int i = 1; i < ordered.Length; i++)
        {
            if (ordered[i - 1].Head.Right.X >= ordered[i].Head.Left.X)
            {
                throw new InvalidDataException("角鋼 X 範圍重疊，無法確認頭尾角點對應，請分別框選。");
            }
        }

        var irons = new List<MeasuredAngleIron>();
        for (int i = 0; i < ordered.Length; i++)
        {
            var head = ordered[i].Head;
            var tail = ordered[i].Tail;
            // Keep the public DAS dimensions defined by the returned corners:
            // width is their chord, and height is measured above that chord.
            double width = Distance(head.Left, head.Right);
            var baseline = head.Right.X - head.Left.X;
            double atApex = head.Left.Z + (head.Right.Z - head.Left.Z) * (head.Apex.X - head.Left.X) / baseline;
            double leftLength = Distance(head.Left, tail.Left);
            double rightLength = Distance(head.Right, tail.Right);
            irons.Add(new MeasuredAngleIron
            {
                Index = i + 1,
                HeadLeft = ToPoint(head.Left),
                HeadRight = ToPoint(head.Right),
                HeadApex = ToPoint(head.Apex),
                TailLeft = ToPoint(tail.Left),
                TailRight = ToPoint(tail.Right),
                TailApex = ToPoint(tail.Apex),
                Width = width,
                Height = head.Apex.Z - atApex,
                Length = Distance(head.Apex, tail.Apex),
                LeftWidth = Distance(head.Apex, head.Left),
                RightWidth = Distance(head.Apex, head.Right),
                LeftLength = leftLength,
                RightLength = rightLength,
            });
        }

        return irons;
    }

    static MeasuredPoint ToPoint(Vertex p)
    {
        return new MeasuredPoint
        {
            X = p.X,
            Y = p.Y,
            Z = p.Z,
        };
    }

    static double Distance(Vertex a, Vertex b)
    {
        return Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2) + Math.Pow(a.Z - b.Z, 2));
    }

    sealed record Features(Vertex Left, Vertex Apex, Vertex Right)
    {
        public double CenterX
        {
            get { return Apex.X; }
        }

        public double Width
        {
            get { return Right.X - Left.X; }
        }

        public double Y
        {
            get { return Apex.Y; }
        }
    }

    sealed class FeatureTrack
    {
        Features minimum;
        Features maximum;
        readonly double firstY;
        int orientation;

        public FeatureTrack(Features first)
        {
            minimum = first;
            maximum = first;
            firstY = first.Y;
            CenterX = first.CenterX;
            Width = first.Width;
            Samples = 1;
        }

        public Features Head
        {
            get { return orientation < 0 ? maximum : minimum; }
        }

        public Features Tail
        {
            get { return orientation < 0 ? minimum : maximum; }
        }

        public double CenterX { get; }

        public double Width { get; }

        public int Samples { get; private set; }

        public void Observe(Features feature)
        {
            if (orientation == 0 && Math.Abs(feature.Y - firstY) > 1e-9)
            {
                orientation = Math.Sign(feature.Y - firstY);
            }

            if (feature.Y < minimum.Y)
            {
                minimum = feature;
            }

            if (feature.Y > maximum.Y)
            {
                maximum = feature;
            }

            Samples++;
        }
    }
}
