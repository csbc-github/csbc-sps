namespace ThreeDScan.Infrastructure.LineScan;

/// <summary>
/// 與 Hank <c>PointMean</c> 相同：同一條射線先剔除距離離群，再平均座標。
/// 射線用錄製檔裡的編號對齊，不用角度。
/// </summary>
internal sealed class LineScanFrameAverager
{
    /// <summary>同一群裡允許的距離差 (mm)。與 Python <c>edge_mm</c> 相同。</summary>
    public const double EdgeMm = 10;

    private int _frames;
    private readonly Dictionary<int, List<RayHit>> _rays = new();

    /// <summary>加入一圈。沒有點的圈也要算進分母。同一編號是同一條射線。</summary>
    public void Add(IReadOnlyList<(int Index, double X, double Y)> points)
    {
        _frames++;
        foreach (var (index, x, y) in points)
        {
            if (!double.IsFinite(x) || !double.IsFinite(y))
            {
                continue;
            }

            if (!_rays.TryGetValue(index, out var samples))
            {
                samples = new List<RayHit>();
                _rays[index] = samples;
            }

            samples.Add(new RayHit
            {
                Radius = Math.Sqrt(x * x + y * y),
                Frame = _frames,
                X = x,
                Y = y,
            });
        }
    }

    /// <summary>輸出通過門檻的射線平均點。清空後可再收下一批。</summary>
    public List<(double X, double Y)> Take()
    {
        var result = new List<(double X, double Y)>();
        if (_frames == 0)
        {
            return result;
        }

        var minimum = (int)Math.Ceiling(_frames * 0.6);
        var recentCount = Math.Min(_frames, Math.Max(3, (int)Math.Ceiling(_frames * 0.25)));
        var recentStart = _frames - recentCount;
        var recentMinimum = (int)Math.Ceiling(recentCount * 0.6);
        foreach (var samples in _rays.Values)
        {
            if (samples.Count < minimum)
            {
                continue;
            }

            samples.Sort(CompareRadius);
            var left = 0;
            var bestStart = 0;
            var bestEnd = 0;
            for (var right = 0; right < samples.Count; right++)
            {
                while (samples[right].Radius - samples[left].Radius > EdgeMm)
                {
                    left++;
                }

                if (right + 1 - left > bestEnd - bestStart)
                {
                    bestStart = left;
                    bestEnd = right + 1;
                }
            }

            var count = bestEnd - bestStart;
            if (count < minimum)
            {
                continue;
            }

            var recent = 0;
            double sumX = 0;
            double sumY = 0;
            for (var i = bestStart; i < bestEnd; i++)
            {
                sumX += samples[i].X;
                sumY += samples[i].Y;
                if (samples[i].Frame > recentStart)
                {
                    recent++;
                }
            }

            if (recent < recentMinimum)
            {
                continue;
            }

            result.Add((sumX / count, sumY / count));
        }

        Clear();
        return result;
    }

    /// <summary>丟掉尚未輸出的圈。</summary>
    public void Clear()
    {
        _frames = 0;
        _rays.Clear();
    }

    private static int CompareRadius(RayHit left, RayHit right)
    {
        return left.Radius.CompareTo(right.Radius);
    }

    private struct RayHit
    {
        public double Radius;
        public int Frame;
        public double X;
        public double Y;
    }
}
